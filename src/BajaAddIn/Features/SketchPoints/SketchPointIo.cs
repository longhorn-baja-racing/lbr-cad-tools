using System;
using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;

namespace Lbr.CadTools.AddIn.Features.SketchPoints
{
    /// <summary>
    /// Puts points into a sketch and reads them back, always in model
    /// coordinates (meters). Kept apart from the commands so the smoke test can
    /// drive it without UI.
    ///
    /// 2D sketch points are stored in sketch space, so both directions go
    /// through ISketch.ModelToSketchTransform. A 3D sketch uses model space.
    /// </summary>
    internal static class SketchPointIo
    {
        /// <summary>Points further than this from a 2D sketch plane are counted as off-plane.</summary>
        public const double PlaneTolerance = 1e-6;

        internal sealed class ImportResult
        {
            public string SketchName { get; set; }
            public bool NewSketch { get; set; }
            public int Added { get; set; }

            /// <summary>Points projected onto a 2D sketch plane because they were not on it.</summary>
            public int OffPlane { get; set; }
        }

        /// <summary>
        /// Adds <paramref name="points"/> to the sketch being edited, or to a new
        /// 3D sketch (closed again afterwards) when no sketch is open.
        /// </summary>
        public static ImportResult Import(ISldWorks app, IModelDoc2 doc, IReadOnlyList<Point3> points)
        {
            ISketchManager sm = doc.SketchManager;
            var result = new ImportResult();

            ISketch sketch = sm.ActiveSketch;
            if (sketch == null)
            {
                sm.Insert3DSketch(true);
                sketch = sm.ActiveSketch ?? throw new InvalidOperationException("Could not start a 3D sketch.");
                result.NewSketch = true;
            }
            result.SketchName = ((IFeature)sketch).Name;

            MathTransform toSketch = sketch.Is3D() ? null : sketch.ModelToSketchTransform;
            var math = (IMathUtility)app.GetMathUtility();

            bool addToDb = sm.AddToDB;
            bool display = sm.DisplayWhenAdded;
            sm.AddToDB = true;
            sm.DisplayWhenAdded = false;
            try
            {
                foreach (Point3 p in points)
                {
                    double x = p.X, y = p.Y, z = p.Z;
                    if (toSketch != null)
                    {
                        double[] s = Transform(math, toSketch, x, y, z);
                        if (Math.Abs(s[2]) > PlaneTolerance)
                        {
                            result.OffPlane++;
                        }
                        x = s[0];
                        y = s[1];
                        z = 0;
                    }
                    if (sm.CreatePoint(x, y, z) != null)
                    {
                        result.Added++;
                    }
                }
            }
            finally
            {
                sm.AddToDB = addToDb;
                sm.DisplayWhenAdded = display;
            }

            if (result.NewSketch)
            {
                sm.Insert3DSketch(true);
            }
            doc.GraphicsRedraw2();
            return result;
        }

        /// <summary>
        /// Every point of <paramref name="sketch"/> in model coordinates,
        /// including the end points of lines and arcs.
        /// </summary>
        public static List<Point3> Export(ISldWorks app, ISketch sketch)
        {
            object[] sketchPoints = sketch.GetSketchPoints2() as object[] ?? new object[0];
            MathTransform toModel = sketch.Is3D() ? null : (MathTransform)sketch.ModelToSketchTransform.Inverse();
            var math = (IMathUtility)app.GetMathUtility();

            var result = new List<Point3>(sketchPoints.Length);
            foreach (ISketchPoint sp in sketchPoints.Cast<ISketchPoint>())
            {
                if (toModel == null)
                {
                    result.Add(new Point3(sp.X, sp.Y, sp.Z));
                }
                else
                {
                    double[] m = Transform(math, toModel, sp.X, sp.Y, sp.Z);
                    result.Add(new Point3(m[0], m[1], m[2]));
                }
            }
            return result;
        }

        /// <summary>
        /// The sketch to export: the one being edited, else a sketch selected in
        /// the FeatureManager tree. Null when there is neither.
        /// </summary>
        public static ISketch FindSketch(IModelDoc2 doc)
        {
            ISketch active = doc.SketchManager.ActiveSketch;
            if (active != null)
            {
                return active;
            }

            var selection = (ISelectionMgr)doc.SelectionManager;
            for (int i = 1; i <= selection.GetSelectedObjectCount2(-1); i++)
            {
                if (selection.GetSelectedObject6(i, -1) is IFeature feature
                    && feature.GetSpecificFeature2() is ISketch sketch)
                {
                    return sketch;
                }
            }
            return null;
        }

        private static double[] Transform(IMathUtility math, MathTransform xform, double x, double y, double z)
        {
            var point = (IMathPoint)math.CreatePoint(new[] { x, y, z });
            point = (IMathPoint)point.MultiplyTransform(xform);
            return (double[])point.ArrayData;
        }
    }
}
