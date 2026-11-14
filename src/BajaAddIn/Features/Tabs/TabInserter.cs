using System;
using System.IO;
using System.Linq;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.Tabs
{
    /// <summary>
    /// Puts a generated tab into an assembly on a picked tube face (issue #35):
    /// reads the face, saves the tab part next to the assembly, inserts it at
    /// <see cref="TabPlacement"/>'s position, and fixes it (it is welded).
    /// </summary>
    internal static class TabInserter
    {
        /// <summary>
        /// The tube face selected in the active assembly, in assembly
        /// coordinates; null when the selection is not one cylindrical or
        /// planar face of a component.
        /// </summary>
        public static TabMount ReadSelection(ISldWorks app, out string componentName)
        {
            componentName = null;
            if (!(app.ActiveDoc is IModelDoc2 doc) || doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return null;
            }
            var sel = (ISelectionMgr)doc.SelectionManager;
            if (sel.GetSelectedObjectCount2(-1) < 1
                || sel.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelFACES
                || !(sel.GetSelectedObject6(1, -1) is IFace2 face))
            {
                return null;
            }
            var component = sel.GetSelectedObjectsComponent4(1, -1) as IComponent2;
            componentName = component?.Name2;
            var mu = (IMathUtility)app.GetMathUtility();
            MathTransform toAssembly = component == null ? null : (component.GetTotalTransform(true) as MathTransform ?? component.Transform2);

            Vec3 Point(double[] p) => Transform(mu, toAssembly, p, isPoint: true);
            Vec3 Direction(double[] v) => Transform(mu, toAssembly, v, isPoint: false);

            var surface = (ISurface)face.GetSurface();
            double[] pickArray = sel.GetSelectionPoint2(1, -1) as double[];

            if (surface.IsCylinder())
            {
                var c = (double[])surface.CylinderParams; // origin, axis, radius
                Vec3 axisPoint = Point(new[] { c[0], c[1], c[2] });
                Vec3 axis = Direction(new[] { c[3], c[4], c[5] });
                double radius = c[6];
                Vec3 pick = pickArray != null && pickArray.Length >= 3
                    ? new Vec3(pickArray[0], pickArray[1], pickArray[2])
                    : FaceMiddle(face, mu, toAssembly);
                TabMount round = TabMount.Round(pick, axisPoint, axis, radius);
                if (Math.Abs(round.PickDistanceFromAxis() - radius) > 1e-4)
                {
                    // Pick not on the surface (API selection without a point): use the top of the tube under it.
                    round = TabMount.Round(ProjectToSurface(round), axisPoint, axis, radius);
                }
                return round;
            }

            if (surface.IsPlane())
            {
                Vec3 normal = Direction((double[])face.Normal);
                Vec3 length = LongestEdgeDirection(face, mu, toAssembly) ?? Perpendicular(normal);
                Vec3 pick = pickArray != null && pickArray.Length >= 3
                    ? new Vec3(pickArray[0], pickArray[1], pickArray[2])
                    : FaceMiddle(face, mu, toAssembly);
                return TabMount.Flat(pick, normal, length);
            }
            return null;
        }

        /// <summary>
        /// Saves <paramref name="tab"/> next to the assembly, closes it, inserts
        /// it at <paramref name="frame"/>, and fixes it. Returns the new component.
        /// </summary>
        public static IComponent2 Insert(ISldWorks app, IModelDoc2 assembly, IModelDoc2 tab, TabUse use, TabFrame frame)
        {
            string folder = Path.GetDirectoryName(assembly.GetPathName());
            string name = TabPlacement.FileName(use, n => File.Exists(Path.Combine(folder, n + ".SLDPRT")));
            string path = Path.Combine(folder, name + ".SLDPRT");

            int errors = 0, warnings = 0;
            if (!tab.Extension.SaveAs3(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, null, ref errors, ref warnings))
            {
                throw new InvalidOperationException($"Could not save the tab as {path} (error {errors}).");
            }

            // Back to the assembly; AddComponent5 needs the part open, so the tab stays loaded in the background.
            int activateErrors = 0;
            app.ActivateDoc3(assembly.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateErrors);

            var asm = (IAssemblyDoc)assembly;
            IComponent2 component = asm.AddComponent5(path,
                (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig, "", false, "",
                frame.Origin.X, frame.Origin.Y, frame.Origin.Z)
                ?? throw new InvalidOperationException($"Could not insert {path} into the assembly.");

            var mu = (IMathUtility)app.GetMathUtility();
            component.Transform2 = (MathTransform)mu.CreateTransform(frame.ToSolidWorks());

            assembly.ClearSelection2(true);
            component.Select4(false, null, false);
            asm.FixComponent();
            assembly.ClearSelection2(true);
            assembly.EditRebuild3();
            app.CloseDoc(path); // closes the part window only; the component stays loaded
            return component;
        }

        private static Vec3 Transform(IMathUtility mu, MathTransform xf, double[] v, bool isPoint)
        {
            if (xf == null)
            {
                return new Vec3(v[0], v[1], v[2]);
            }
            double[] r = isPoint
                ? (double[])((IMathPoint)((IMathPoint)mu.CreatePoint(v)).MultiplyTransform(xf)).ArrayData
                : (double[])((IMathVector)((IMathVector)mu.CreateVector(v)).MultiplyTransform(xf)).ArrayData;
            return new Vec3(r[0], r[1], r[2]);
        }

        private static Vec3 ProjectToSurface(TabMount m)
        {
            Vec3 d = m.Pick - m.AxisPoint;
            Vec3 foot = m.AxisPoint + m.Axis * d.Dot(m.Axis);
            Vec3 radial = m.Pick - foot;
            Vec3 dir = radial.Length > 1e-9 ? radial.Unit() : Perpendicular(m.Axis);
            return foot + dir * m.Radius;
        }

        private static Vec3? LongestEdgeDirection(IFace2 face, IMathUtility mu, MathTransform xf)
        {
            Vec3? best = null;
            double bestLength = 0;
            foreach (IEdge edge in ((face.GetEdges() as object[]) ?? new object[0]).OfType<IEdge>())
            {
                var curve = (ICurve)edge.GetCurve();
                if (!curve.IsLine())
                {
                    continue;
                }
                var start = edge.GetStartVertex() as IVertex;
                var end = edge.GetEndVertex() as IVertex;
                if (start == null || end == null)
                {
                    continue;
                }
                Vec3 a = Transform(mu, xf, (double[])start.GetPoint(), isPoint: true);
                Vec3 b = Transform(mu, xf, (double[])end.GetPoint(), isPoint: true);
                double length = (b - a).Length;
                if (length > bestLength)
                {
                    bestLength = length;
                    best = (b - a).Unit();
                }
            }
            return best;
        }

        private static Vec3 FaceMiddle(IFace2 face, IMathUtility mu, MathTransform xf)
        {
            var box = (double[])face.GetBox();
            return Transform(mu, xf, new[] { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 }, isPoint: true);
        }

        private static Vec3 Perpendicular(Vec3 a)
        {
            Vec3 trial = Math.Abs(a.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
            return a.Cross(trial).Unit();
        }
    }
}
