using System;
using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.Suspension
{
    /// <summary>
    /// Finds reference points named HP_&lt;point&gt;_&lt;corner&gt; in the active
    /// document and its components, in model (assembly) coordinates, and turns
    /// them into alignment numbers. Kept apart from the command so the smoke
    /// test can drive it.
    /// </summary>
    internal static class HardpointReader
    {
        internal sealed class Result
        {
            /// <summary>Point position (meters, model coordinates) by full name, e.g. "HP_UBJ_FL".</summary>
            public Dictionary<string, Vec3> Points { get; } = new Dictionary<string, Vec3>(StringComparer.OrdinalIgnoreCase);

            public List<string> Problems { get; } = new List<string>();
        }

        /// <summary>One HP_ reference point found in the model, kept so its position can be re-read cheaply.</summary>
        internal sealed class Tracked
        {
            public string Name { get; set; }

            public IRefPoint Point { get; set; }

            /// <summary>The component holding it, or null when it is in the document itself.</summary>
            public IComponent2 Component { get; set; }

            public string Source { get; set; }
        }

        /// <summary>The HP_ points found once by <see cref="Find"/>, plus problems found while looking.</summary>
        internal sealed class Found
        {
            public List<Tracked> Points { get; } = new List<Tracked>();

            public List<string> Problems { get; } = new List<string>();
        }

        public static Result Read(ISldWorks app, IModelDoc2 doc) => Evaluate(Find(doc));

        /// <summary>
        /// Positions of points found earlier, in model coordinates, using each
        /// component's current transform. Cheap enough to call while dragging.
        /// </summary>
        public static Result Evaluate(Found found)
        {
            var result = new Result();
            result.Problems.AddRange(found.Problems);
            foreach (Tracked t in found.Points)
            {
                if (!(t.Point.GetRefPoint() is IMathPoint mp))
                {
                    result.Problems.Add($"{t.Name} in {t.Source}: could not read its position.");
                    continue;
                }
                if (t.Component != null)
                {
                    mp = (IMathPoint)mp.MultiplyTransform(t.Component.Transform2);
                }
                var xyz = (double[])mp.ArrayData;
                result.Points[t.Name] = new Vec3(xyz[0], xyz[1], xyz[2]);
            }
            return result;
        }

        /// <summary>Finds every HP_ reference point in the document and its components (the slow part).</summary>
        public static Found Find(IModelDoc2 doc)
        {
            var result = new Found();
            var where = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Collect(IModelDoc2 model, IComponent2 component, string source)
            {
                foreach (IFeature f in ((model.FeatureManager.GetFeatures(false) as object[]) ?? new object[0]).OfType<IFeature>())
                {
                    string name;
                    try
                    {
                        if (f.GetTypeName2() != "RefPoint")
                        {
                            continue;
                        }
                        name = f.Name;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        continue;
                    }
                    if (!Hardpoints.TryParse(name, out _, out _))
                    {
                        continue;
                    }
                    if (!(f.GetSpecificFeature2() is IRefPoint refPoint))
                    {
                        result.Problems.Add($"{name} in {source}: could not read its position.");
                        continue;
                    }
                    if (where.TryGetValue(name, out string first))
                    {
                        result.Problems.Add($"{name} is in both {first} and {source}; using the first. " +
                                            "Name points per corner, for example in the assembly instead of a shared part.");
                        continue;
                    }
                    where[name] = source;
                    result.Points.Add(new Tracked { Name = name, Point = refPoint, Component = component, Source = source });
                }
            }

            Collect(doc, null, "this document");
            if (doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                object[] components = ((IAssemblyDoc)doc).GetComponents(false) as object[] ?? new object[0];
                foreach (IComponent2 c in components.Cast<IComponent2>())
                {
                    if (c.IsSuppressed() || !(c.GetModelDoc2() is IModelDoc2 model))
                    {
                        continue;
                    }
                    Collect(model, c, c.Name2);
                }
            }
            return result;
        }

        /// <summary>
        /// Alignment for every corner that has all its required points, plus
        /// steering when both fronts are there. Missing points go to problems.
        /// </summary>
        public static (List<CornerGeometry> Corners, SteeringGeometry Steering) Compute(
            Result read, SuspensionSettings settings, List<string> problems)
        {
            var axes = new VehicleAxes(settings.ForwardAxis, settings.UpAxis);
            double tireRadius = settings.TireRadiusInches * Units.MetersPerInch;
            var corners = new List<CornerGeometry>();
            var wheelCenters = new Dictionary<Corner, Vec3>();

            foreach (Corner corner in (Corner[])Enum.GetValues(typeof(Corner)))
            {
                List<string> names = Hardpoints.Required.Select(p => Hardpoints.Name(p, corner)).ToList();
                List<string> missing = names.Where(n => !read.Points.ContainsKey(n)).ToList();
                Vec3 V(string point) => axes.ToVehicle(read.Points[Hardpoints.Name(point, corner)]);
                if (missing.Count == names.Count)
                {
                    continue; // corner not modeled at all
                }
                bool rear = corner == Corner.RL || corner == Corner.RR;
                if (rear && missing.Count == names.Count - 1 && read.Points.ContainsKey(Hardpoints.Name(Hardpoints.Wc, corner)))
                {
                    wheelCenters[corner] = V(Hardpoints.Wc); // only locates the rear axle
                    continue;
                }
                if (missing.Count > 0)
                {
                    problems.Add($"{corner}: missing {string.Join(", ", missing)}.");
                    continue;
                }
                var points = new CornerPoints
                {
                    Corner = corner,
                    UpperBallJoint = V(Hardpoints.Ubj),
                    LowerBallJoint = V(Hardpoints.Lbj),
                    WheelCenter = V(Hardpoints.Wc),
                    SpinAxisPoint = V(Hardpoints.Sa),
                    TireRadius = tireRadius,
                };
                try
                {
                    corners.Add(SuspensionMath.Corner(points));
                    wheelCenters[corner] = points.WheelCenter;
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"{corner}: {ex.Message}");
                }
            }

            SteeringGeometry steering = null;
            CornerGeometry fl = corners.FirstOrDefault(c => c.Corner == Corner.FL);
            CornerGeometry fr = corners.FirstOrDefault(c => c.Corner == Corner.FR);
            if (fl != null && fr != null)
            {
                List<Vec3> rear = wheelCenters.Where(p => p.Key == Corner.RL || p.Key == Corner.RR).Select(p => p.Value).ToList();
                if (rear.Count > 0)
                {
                    steering = SuspensionMath.Steering(fl, fr, rear.Average(v => v.X));
                }
                else
                {
                    problems.Add("Turning radius and Ackermann need a rear wheel center (HP_WC_RL or HP_WC_RR).");
                }
            }
            return (corners, steering);
        }
    }
}
