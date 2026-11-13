using System;
using System.Linq;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.Tabs
{
    /// <summary>
    /// Builds a B.12.2 attachment tab as a new, unsaved part: the Figure B-54
    /// outline sketched on the Front plane and extruded to thickness t. A
    /// double shear pair is two bodies, the second starting t + gap from the
    /// plane. Kept apart from the command so the smoke test can drive it.
    /// </summary>
    internal static class TabBuilder
    {
        public static IModelDoc2 Build(ISldWorks app, TabInputs t)
        {
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            var doc = (IModelDoc2)app.NewDocument(template, 0, 0, 0)
                ?? throw new InvalidOperationException($"Could not create a part from the template '{template}'.");

            Extrude(doc, t, startOffset: 0, merge: true);
            if (t.DoubleShear)
            {
                Extrude(doc, t, startOffset: t.Thickness + t.Gap, merge: false);
            }

            ICustomPropertyManager props = doc.Extension.get_CustomPropertyManager("");
            string issues = string.Join("; ", TabRules.Check(t).Select(i => i.Message));
            SetProperty(props, "Description", TabRules.Describe(t));
            SetProperty(props, "Tab Rule Check", issues.Length == 0 ? "OK (B.12.2)" : issues);

            doc.ClearSelection2(true);
            doc.ViewZoomtofit2();
            return doc;
        }

        private static void Extrude(IModelDoc2 doc, TabInputs t, double startOffset, bool merge)
        {
            IFeature front = FirstPlane(doc);
            front.Select2(false, 0);
            ISketchManager sm = doc.SketchManager;
            sm.InsertSketch(true);

            bool addToDb = sm.AddToDB;
            sm.AddToDB = true; // no snapping or inferred relations while drawing
            try
            {
                DrawProfile(sm, t);
            }
            finally
            {
                sm.AddToDB = addToDb;
            }
            sm.InsertSketch(true);

            ((IFeature)doc.FeatureByPositionReverse(0)).Select2(false, 0);
            IFeature extrude = doc.FeatureManager.FeatureExtrusion3(
                true, false, false,
                (int)swEndConditions_e.swEndCondBlind, 0, t.Thickness, 0,
                false, false, false, false, 0, 0,
                false, false, false, false,
                merge, true, true,
                startOffset > 0 ? (int)swStartConditions_e.swStartOffset : (int)swStartConditions_e.swStartSketchPlane,
                startOffset, false);
            if (extrude == null)
            {
                throw new InvalidOperationException("The tab extrusion failed.");
            }
        }

        private static void DrawProfile(ISketchManager sm, TabInputs t)
        {
            double b = t.BaseWidth / 2, l = t.Length, r = t.EndDiameter / 2;
            (double tx, double ty) = TabGeometry.RightTangentPoint(t);

            if (t.ContourTubeDiameter > 0)
            {
                // Concave base: tube arc through both corners, bulging up into the tab.
                double rt = t.ContourTubeDiameter / 2;
                double c = Math.Sqrt(rt * rt - b * b);
                sm.CreateArc(0, -c, 0, b, 0, 0, -b, 0, 0, 1);
            }
            else
            {
                sm.CreateLine(-b, 0, 0, b, 0, 0);
            }
            sm.CreateLine(b, 0, 0, tx, ty, 0);
            sm.CreateArc(0, l, 0, tx, ty, 0, -tx, ty, 0, 1); // counterclockwise, over the top
            sm.CreateLine(-tx, ty, 0, -b, 0, 0);
            sm.CreateCircleByRadius(0, l, 0, t.Hole / 2);
        }

        private static IFeature FirstPlane(IModelDoc2 doc)
        {
            for (var f = (IFeature)doc.FirstFeature(); f != null; f = (IFeature)f.GetNextFeature())
            {
                string type;
                try
                {
                    type = f.GetTypeName2();
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    continue; // seen out of process right after documents close; skip that feature
                }
                if (type == "RefPlane")
                {
                    return f; // Front plane; found by type so the template language does not matter
                }
            }
            throw new InvalidOperationException("The part template has no reference plane.");
        }

        private static void SetProperty(ICustomPropertyManager props, string name, string value) =>
            props.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, value,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
    }
}
