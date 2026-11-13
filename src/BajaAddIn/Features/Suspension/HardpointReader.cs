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

        public static Result Read(ISldWorks app, IModelDoc2 doc)
        {
            var result = new Result();
            var math = (IMathUtility)app.GetMathUtility();
            var where = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Collect(IModelDoc2 model, MathTransform toAssembly, string source)
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
                    if (!(f.GetSpecificFeature2() is IRefPoint refPoint) || !(refPoint.GetRefPoint() is IMathPoint mp))
                    {
                        result.Problems.Add($"{name} in {source}: could not read its position.");
                        continue;
                    }
                    if (toAssembly != null)
                    {
                        mp = (IMathPoint)mp.MultiplyTransform(toAssembly);
                    }
                    var xyz = (double[])mp.ArrayData;
                    if (where.TryGetValue(name, out string first))
                    {
                        result.Problems.Add($"{name} is in both {first} and {source}; using the first. " +
                                            "Name points per corner, for example in the assembly instead of a shared part.");
                        continue;
                    }
                    where[name] = source;
                    result.Points[name] = new Vec3(xyz[0], xyz[1], xyz[2]);
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
                    Collect(model, c.Transform2, c.Name2);
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
