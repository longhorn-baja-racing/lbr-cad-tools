using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Lbr.CadTools.AddIn.Features.FixComponents
{
    /// <summary>
    /// Fixes or floats the top-level components of an assembly. Kept apart from
    /// the command so the smoke test can drive it without UI.
    ///
    /// Known limits (verified on SW 2026 SP5):
    /// - Fixing applies to all configurations.
    /// - IAssemblyDoc.FixComponent / UnfixComponent are not recorded on the
    ///   undo stack, so Ctrl+Z cannot undo them. Running the built-in
    ///   Fix/Unfix commands (swCommands_e 957/959) through RunCommand does not
    ///   act on the selection, in process or out, so it is no alternative.
    /// </summary>
    internal static class ComponentFixer
    {
        internal sealed class Result
        {
            public int TopLevel { get; set; }
            public int Changed { get; set; }
            public int Unchanged { get; set; }
            public List<string> Failed { get; } = new List<string>();
        }

        public static Result Apply(IModelDoc2 doc, bool fix)
        {
            var assembly = (IAssemblyDoc)doc;
            List<IComponent2> components = (assembly.GetComponents(true) as object[] ?? new object[0])
                .Cast<IComponent2>()
                .ToList();
            List<IComponent2> toChange = components.Where(c => c.IsFixed() != fix).ToList();

            if (toChange.Count > 0)
            {
                doc.ClearSelection2(true);
                foreach (IComponent2 component in toChange)
                {
                    component.Select4(true, null, false);
                }
                if (fix)
                {
                    assembly.FixComponent();
                }
                else
                {
                    assembly.UnfixComponent();
                }
                doc.ClearSelection2(true);
                doc.GraphicsRedraw2();
            }

            // Re-read the state: pattern instances and similar components can
            // refuse the change without an error.
            var result = new Result
            {
                TopLevel = components.Count,
                Unchanged = components.Count - toChange.Count,
            };
            result.Failed.AddRange(toChange.Where(c => c.IsFixed() != fix).Select(c => c.Name2));
            result.Changed = toChange.Count - result.Failed.Count;
            return result;
        }
    }
}
