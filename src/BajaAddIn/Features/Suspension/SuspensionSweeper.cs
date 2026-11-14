using System;
using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.Suspension
{
    /// <summary>
    /// Suspension travel sweep (issue #12): steps a driving dimension through a
    /// range, rebuilds, and reads the HP_ points at each step. The dimension is
    /// always put back to its starting value.
    /// </summary>
    internal static class SuspensionSweeper
    {
        internal sealed class Driver
        {
            public IDimension Dimension { get; set; }

            /// <summary>Full name, e.g. "D1@Sketch3@Suspension.Part".</summary>
            public string Name { get; set; }

            public bool Angular { get; set; }

            /// <summary>Value before the sweep (meters or radians).</summary>
            public double Start { get; set; }
        }

        internal sealed class Result
        {
            /// <summary>The suspension before the sweep; wheel travel is measured from here.</summary>
            public List<CornerGeometry> Reference { get; } = new List<CornerGeometry>();

            public List<SweepStep> Steps { get; } = new List<SweepStep>();

            public List<string> Problems { get; } = new List<string>();
        }

        /// <summary>The dimension selected in the document, or null.</summary>
        public static Driver Selected(IModelDoc2 doc)
        {
            var sel = (ISelectionMgr)doc.SelectionManager;
            if (sel.GetSelectedObjectCount2(-1) < 1
                || sel.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelDIMENSIONS
                || !(sel.GetSelectedObject6(1, -1) is IDisplayDimension display))
            {
                return null;
            }
            return FromDimension(display.GetDimension2(0));
        }

        public static Driver FromDimension(IDimension dim)
        {
            if (dim == null)
            {
                return null;
            }
            var values = dim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
            int type = dim.GetType();
            if (values == null || values.Length == 0 || type == (int)swDimensionParamType_e.swDimensionParamTypeInteger)
            {
                return null;
            }
            return new Driver
            {
                Dimension = dim,
                Name = dim.FullName,
                Angular = type == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular,
                Start = values[0],
            };
        }

        public static Result Run(ISldWorks app, IModelDoc2 doc, Driver driver, IReadOnlyList<double> values,
                                 SuspensionSettings settings, Action<int> progress = null)
        {
            var result = new Result();
            result.Reference.AddRange(Read(app, doc, settings, result.Problems, label: "start"));
            if (result.Reference.Count == 0)
            {
                result.Problems.Insert(0, "No complete corner found at the start. Add HP_ reference points (see Suspension Check).");
                return result;
            }
            try
            {
                for (int i = 0; i < values.Count; i++)
                {
                    progress?.Invoke(i);
                    int status = driver.Dimension.SetSystemValue3(values[i],
                        (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                    if (status != (int)swSetValueReturnStatus_e.swSetValue_Successful)
                    {
                        result.Problems.Add(status == (int)swSetValueReturnStatus_e.swSetValue_DrivenDimension
                            ? $"{driver.Name} is a driven (reference) dimension; pick one that drives the geometry."
                            : $"Step {i + 1}: SolidWorks would not set {driver.Name} (status {status}).");
                        if (status == (int)swSetValueReturnStatus_e.swSetValue_DrivenDimension)
                        {
                            break;
                        }
                        continue;
                    }
                    doc.ForceRebuild3(false);
                    List<CornerGeometry> corners = Read(app, doc, settings, result.Problems, label: $"step {i + 1}");
                    if (corners.Count > 0)
                    {
                        result.Steps.Add(new SweepStep(values[i], corners));
                    }
                }
            }
            finally
            {
                driver.Dimension.SetSystemValue3(driver.Start, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                doc.ForceRebuild3(false);
            }
            return result;
        }

        private static List<CornerGeometry> Read(ISldWorks app, IModelDoc2 doc, SuspensionSettings settings, List<string> problems, string label)
        {
            HardpointReader.Result read = HardpointReader.Read(app, doc);
            var stepProblems = new List<string>(read.Problems);
            (List<CornerGeometry> corners, _) = HardpointReader.Compute(read, settings, stepProblems);
            foreach (string p in stepProblems.Where(p => !problems.Any(q => q.EndsWith(p, StringComparison.Ordinal))))
            {
                problems.Add($"{label}: {p}");
            }
            return corners;
        }
    }
}
