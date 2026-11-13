using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Lbr.CadTools.AddIn.Features.FixComponents
{
    /// <summary>
    /// Fixes or floats the top-level components of an assembly, and undoes the
    /// last such change. Kept apart from the commands so the smoke test can
    /// drive it without UI.
    ///
    /// Known limits (verified on SW 2026 SP5):
    /// - Fixing applies to all configurations.
    /// - IAssemblyDoc.FixComponent / UnfixComponent are not recorded on the
    ///   undo stack, so Ctrl+Z cannot undo them. Running the built-in
    ///   Fix/Unfix commands (swCommands_e 957/959) through RunCommand does not
    ///   act on the selection, in process or out, so it is no alternative.
    ///   <see cref="Restore"/> with <see cref="History"/> stands in for undo.
    /// </summary>
    internal static class ComponentFixer
    {
        internal sealed class Result
        {
            public int TopLevel { get; set; }
            public int Changed { get; set; }
            public int Unchanged { get; set; }
            public List<string> Failed { get; } = new List<string>();

            /// <summary>Names (Name2) of the components that were changed.</summary>
            public List<string> ChangedNames { get; } = new List<string>();
        }

        /// <summary>What the last Fix All / Float All changed, per open assembly.</summary>
        internal sealed class HistoryEntry
        {
            public bool Fix { get; set; }
            public List<string> Names { get; set; }
        }

        /// <summary>
        /// Last change per assembly, keyed by <see cref="Key"/>. Entries are not
        /// cleared when a document closes; <see cref="Restore"/> only touches
        /// components still in the state the change left them in, so a stale
        /// entry cannot do harm.
        /// </summary>
        internal static Dictionary<string, HistoryEntry> History { get; } =
            new Dictionary<string, HistoryEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>History key: the file path, or the title of an unsaved document.</summary>
        public static string Key(IModelDoc2 doc)
        {
            string path = doc.GetPathName();
            return string.IsNullOrEmpty(path) ? "unsaved:" + doc.GetTitle() : path;
        }

        public static Result Apply(IModelDoc2 doc, bool fix)
        {
            List<IComponent2> components = TopLevel(doc);
            List<IComponent2> toChange = components.Where(c => c.IsFixed() != fix).ToList();
            SetFixed(doc, toChange, fix);

            // Re-read the state: pattern instances and similar components can
            // refuse the change without an error.
            var result = new Result
            {
                TopLevel = components.Count,
                Unchanged = components.Count - toChange.Count,
            };
            foreach (IComponent2 c in toChange)
            {
                (c.IsFixed() == fix ? result.ChangedNames : result.Failed).Add(c.Name2);
            }
            result.Changed = result.ChangedNames.Count;

            if (result.Changed > 0)
            {
                History[Key(doc)] = new HistoryEntry { Fix = fix, Names = result.ChangedNames.ToList() };
            }
            return result;
        }

        /// <summary>
        /// Undoes the last <see cref="Apply"/> on this assembly: components it
        /// changed that are still in that state are flipped back. Clears the entry.
        /// </summary>
        /// <returns>Null when there is nothing recorded for this assembly.</returns>
        public static (bool LastWasFix, int Restored, int Skipped)? Restore(IModelDoc2 doc)
        {
            string key = Key(doc);
            if (!History.TryGetValue(key, out HistoryEntry entry))
            {
                return null;
            }
            History.Remove(key);

            var byName = TopLevel(doc).ToDictionary(c => c.Name2, StringComparer.OrdinalIgnoreCase);
            List<IComponent2> toRestore = entry.Names
                .Where(n => byName.TryGetValue(n, out IComponent2 c) && c.IsFixed() == entry.Fix)
                .Select(n => byName[n])
                .ToList();
            SetFixed(doc, toRestore, !entry.Fix);

            int restored = toRestore.Count(c => c.IsFixed() != entry.Fix);
            return (entry.Fix, restored, entry.Names.Count - restored);
        }

        private static List<IComponent2> TopLevel(IModelDoc2 doc) =>
            (((IAssemblyDoc)doc).GetComponents(true) as object[] ?? new object[0]).Cast<IComponent2>().ToList();

        private static void SetFixed(IModelDoc2 doc, List<IComponent2> components, bool fix)
        {
            if (components.Count == 0)
            {
                return;
            }
            var assembly = (IAssemblyDoc)doc;
            doc.ClearSelection2(true);
            foreach (IComponent2 component in components)
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
    }
}
