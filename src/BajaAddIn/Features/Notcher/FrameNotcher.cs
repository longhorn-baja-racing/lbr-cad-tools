using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.Notcher
{
    /// <summary>
    /// Ordered tube notcher (issue #11) for weldment frames built from
    /// structural members: reads the tube bodies and how they meet, and makes
    /// one coped Trim/Extend feature per tube from a <see cref="NotchPlan"/>.
    /// The decisions live in Core <see cref="Notching"/>.
    /// </summary>
    internal static class FrameNotcher
    {
        /// <summary>Feature type name of a structural member.</summary>
        public const string MemberFeatureType = "WeldMemberFeat";

        /// <summary>An overlap within this distance of a tube's end counts as at the end.</summary>
        private const double EndTolerance = 0.0005;

        internal sealed class Frame
        {
            public List<FrameMember> Members { get; } = new List<FrameMember>();

            /// <summary>Structural member feature names in tree order.</summary>
            public List<string> Groups { get; } = new List<string>();

            public List<FrameJoint> Joints { get; } = new List<FrameJoint>();

            /// <summary>Notch features from an earlier run (they were rolled back before reading).</summary>
            public int OldNotches { get; set; }

            /// <summary>Other Trim/Extend features in the part, made by hand.</summary>
            public int OtherTrims { get; set; }

            /// <summary>Tube bodies whose axis could not be found (not round or rectangular).</summary>
            public List<string> Skipped { get; } = new List<string>();
        }

        public static bool IsWeldmentPart(IModelDoc2 doc) =>
            doc != null && doc.GetType() == (int)swDocumentTypes_e.swDocPART && Features(doc).Any(f => TypeOf(f) == MemberFeatureType);

        /// <summary>
        /// Reads the frame. Notch features from an earlier run are deleted first so
        /// the joints are measured on the untrimmed tubes.
        /// </summary>
        public static Frame Read(IModelDoc2 doc) => Read(doc, deleteOld: true);

        /// <summary>Reads the frame as it is, notches included (checks after notching).</summary>
        public static Frame ReadWithoutDeleting(IModelDoc2 doc) => Read(doc, deleteOld: false);

        private static Frame Read(IModelDoc2 doc, bool deleteOld)
        {
            var frame = new Frame();
            frame.OldNotches = deleteOld ? DeleteOldNotches(doc) : 0;
            frame.OtherTrims = Features(doc).Count(f => TypeOf(f) == "WeldmentTrimFeature" || TypeOf(f) == "TrimExtendFeat");

            frame.Groups.AddRange(Features(doc).Where(f => TypeOf(f) == MemberFeatureType).Select(f => f.Name));

            var tubes = new List<(IBody2 Body, string Name, Vec3 Axis, double Min, double Max, double[] Box)>();
            foreach (IBody2 body in Bodies(doc))
            {
                string group = OwningMember(body);
                if (group == null)
                {
                    continue; // gussets, plates, end caps
                }
                string name = body.Name;
                Vec3? axis = Axis(body);
                if (axis == null)
                {
                    frame.Skipped.Add(name);
                    continue;
                }
                (double min, double max) = Extent(body, axis.Value);
                frame.Members.Add(new FrameMember(name, group));
                tubes.Add((body, name, axis.Value, min, max, (double[])body.GetBodyBox()));
            }

            Dictionary<string, string> groupOf = frame.Members.ToDictionary(m => m.Body, m => m.Group);
            for (int i = 0; i < tubes.Count; i++)
            {
                for (int k = i + 1; k < tubes.Count; k++)
                {
                    var a = tubes[i];
                    var b = tubes[k];
                    if (groupOf[a.Name] == groupOf[b.Name] || !BoxesOverlap(a.Box, b.Box, 0.001))
                    {
                        continue;
                    }
                    List<IBody2> overlap = Intersect(a.Body, b.Body);
                    if (overlap.Count == 0)
                    {
                        continue;
                    }
                    (double aMin, double aMax) = Extent(overlap, a.Axis);
                    (double bMin, double bMax) = Extent(overlap, b.Axis);
                    frame.Joints.Add(new FrameJoint(a.Name, b.Name,
                        Notching.ReachesEnd(a.Min, a.Max, aMin, aMax, EndTolerance),
                        Notching.ReachesEnd(b.Min, b.Max, bMin, bMax, EndTolerance)));
                }
            }
            return frame;
        }

        /// <summary>Makes the trim features of <paramref name="plan"/>. Returns how many were made, and problems.</summary>
        public static (int Made, List<string> Problems) Apply(IModelDoc2 doc, NotchPlan plan)
        {
            var problems = new List<string>();
            int made = 0;
            int options = (int)(swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_CopedCut |
                                swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_WeldGap);
            foreach (KeyValuePair<string, List<string>> step in plan.TrimOrder)
            {
                // Body objects go stale as features are added; look them up by name each time.
                Dictionary<string, IBody2> bodies = Bodies(doc).GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First());
                if (!bodies.TryGetValue(step.Key, out IBody2 trimmed))
                {
                    problems.Add($"{step.Key}: body not found.");
                    continue;
                }
                IBody2[] boundaries = step.Value.Where(bodies.ContainsKey).Select(n => bodies[n]).ToArray();
                if (boundaries.Length == 0)
                {
                    continue;
                }
                doc.ClearSelection2(true);
                IFeature f = doc.FeatureManager.InsertWeldmentTrimFeature2(
                    (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim, options, 0.0,
                    new[] { trimmed }, boundaries);
                if (f == null)
                {
                    problems.Add($"{step.Key}: SolidWorks could not cope it against {string.Join(", ", step.Value)}.");
                    continue;
                }
                f.Name = UniqueName(doc, $"{Notching.FeaturePrefix} - {step.Key}");
                made++;
            }
            doc.ClearSelection2(true);
            doc.EditRebuild3();
            return (made, problems);
        }

        /// <summary>Deletes trim features made by an earlier run; returns how many.</summary>
        public static int DeleteOldNotches(IModelDoc2 doc)
        {
            List<IFeature> old = Features(doc).Where(f => f.Name.StartsWith(Notching.FeaturePrefix, StringComparison.Ordinal)).ToList();
            if (old.Count == 0)
            {
                return 0;
            }
            doc.ClearSelection2(true);
            foreach (IFeature f in old)
            {
                f.Select2(true, 0);
            }
            doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            doc.ClearSelection2(true);
            doc.EditRebuild3();
            return old.Count;
        }

        public static string ReadProperty(IModelDoc2 doc, string name)
        {
            doc.Extension.get_CustomPropertyManager("").Get6(name, false, out _, out string value, out _, out _);
            return value ?? "";
        }

        public static void WriteProperty(IModelDoc2 doc, string name, string value) =>
            doc.Extension.get_CustomPropertyManager("").Add3(name, (int)swCustomInfoType_e.swCustomInfoText, value,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);

        // ---- Geometry ----

        /// <summary>Tube axis: from a cylindrical face, or the cross product of two large non-parallel flat faces.</summary>
        private static Vec3? Axis(IBody2 body)
        {
            var planes = new List<(Vec3 Normal, double Area)>();
            foreach (IFace2 face in ((body.GetFaces() as object[]) ?? new object[0]).OfType<IFace2>())
            {
                var s = (ISurface)face.GetSurface();
                if (s.IsCylinder())
                {
                    var c = (double[])s.CylinderParams;
                    return new Vec3(c[3], c[4], c[5]).Unit();
                }
                if (s.IsPlane())
                {
                    var n = (double[])face.Normal;
                    planes.Add((new Vec3(n[0], n[1], n[2]), face.GetArea()));
                }
            }
            var big = planes.OrderByDescending(p => p.Area).ToList();
            for (int i = 0; i < big.Count; i++)
            {
                for (int k = i + 1; k < big.Count; k++)
                {
                    Vec3 cross = big[i].Normal.Cross(big[k].Normal);
                    if (cross.Length > 0.5)
                    {
                        return cross.Unit();
                    }
                }
            }
            return null;
        }

        private static (double Min, double Max) Extent(IBody2 body, Vec3 axis) => Extent(new[] { body }, axis);

        /// <summary>Lowest and highest projection onto <paramref name="axis"/> over the bodies.</summary>
        private static (double Min, double Max) Extent(IEnumerable<IBody2> bodies, Vec3 axis)
        {
            double min = double.MaxValue, max = double.MinValue;
            foreach (IBody2 b in bodies)
            {
                if (b.GetExtremePoint(axis.X, axis.Y, axis.Z, out double x, out double y, out double z))
                {
                    max = Math.Max(max, new Vec3(x, y, z).Dot(axis));
                }
                if (b.GetExtremePoint(-axis.X, -axis.Y, -axis.Z, out x, out y, out z))
                {
                    min = Math.Min(min, new Vec3(x, y, z).Dot(axis));
                }
            }
            return (min, max);
        }

        /// <summary>The overlap of two bodies as temporary bodies (empty when they do not overlap).</summary>
        private static List<IBody2> Intersect(IBody2 a, IBody2 b)
        {
            try
            {
                var ta = (IBody2)a.Copy();
                var tb = (IBody2)b.Copy();
                object[] result = ta.Operations2((int)swBodyOperationType_e.SWBODYINTERSECT, tb, out int error) as object[];
                return error == 0 && result != null ? result.OfType<IBody2>().ToList() : new List<IBody2>();
            }
            catch (COMException ex)
            {
                Log.Error($"Intersect {a.Name} / {b.Name} failed", ex);
                return new List<IBody2>();
            }
        }

        private static bool BoxesOverlap(double[] a, double[] b, double pad) =>
            a[0] - pad <= b[3] && b[0] - pad <= a[3] &&
            a[1] - pad <= b[4] && b[1] - pad <= a[4] &&
            a[2] - pad <= b[5] && b[2] - pad <= a[5];

        // ---- Tree ----

        private static IEnumerable<IBody2> Bodies(IModelDoc2 doc) =>
            ((((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[]) ?? new object[0]).OfType<IBody2>();

        /// <summary>The structural member feature a body comes from, or null.</summary>
        private static string OwningMember(IBody2 body)
        {
            foreach (IFeature f in ((body.GetFeatures() as object[]) ?? new object[0]).OfType<IFeature>())
            {
                if (TypeOf(f) == MemberFeatureType)
                {
                    return f.Name;
                }
            }
            return null;
        }

        private static IEnumerable<IFeature> Features(IModelDoc2 doc) =>
            ((doc.FeatureManager.GetFeatures(false) as object[]) ?? new object[0]).OfType<IFeature>();

        private static string TypeOf(IFeature f)
        {
            try
            {
                return f.GetTypeName2();
            }
            catch (COMException)
            {
                return "";
            }
        }

        private static string UniqueName(IModelDoc2 doc, string wanted)
        {
            var names = new HashSet<string>(Features(doc).Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            string name = wanted;
            for (int i = 2; names.Contains(name); i++)
            {
                name = $"{wanted} ({i})";
            }
            return name;
        }
    }
}
