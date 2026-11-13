using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lbr.CadTools.Core
{
    /// <summary>What Auto-Save needs to know about one open document.</summary>
    public readonly struct OpenDocInfo
    {
        public OpenDocInfo(string path, bool visible, bool readOnly, bool modified)
        {
            Path = path ?? "";
            Visible = visible;
            ReadOnly = readOnly;
            Modified = modified;
        }

        /// <summary>Empty for a document that was never saved.</summary>
        public string Path { get; }

        /// <summary>
        /// False for components loaded only as part of an open assembly; the
        /// assembly saves them (Save3 with SaveReferenced).
        /// </summary>
        public bool Visible { get; }

        /// <summary>Opened read-only (including PDM files that are not checked out).</summary>
        public bool ReadOnly { get; }

        public bool Modified { get; }
    }

    /// <summary>Which documents Auto-Save saves, and the status bar text.</summary>
    public static class AutoSavePolicy
    {
        /// <summary>Indexes of the documents to save.</summary>
        public static List<int> ToSave(IReadOnlyList<OpenDocInfo> docs) =>
            Enumerable.Range(0, docs.Count)
                .Where(i => docs[i].Visible && docs[i].Modified && !docs[i].ReadOnly && docs[i].Path.Length > 0)
                .ToList();

        /// <summary>Modified documents that cannot be auto-saved because they were never saved.</summary>
        public static int NeverSaved(IReadOnlyList<OpenDocInfo> docs) =>
            docs.Count(d => d.Visible && d.Modified && d.Path.Length == 0);

        public static string Status(DateTime time, int saved, IReadOnlyList<string> failed, int neverSaved)
        {
            string clock = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            string text = saved == 0
                ? $"Auto-save {clock}: nothing to save"
                : $"Auto-saved {saved} {(saved == 1 ? "document" : "documents")} at {clock}";
            if (failed != null && failed.Count > 0)
            {
                text += $"; FAILED: {string.Join(", ", failed)} (see log)";
            }
            if (neverSaved > 0)
            {
                text += $"; {neverSaved} new {(neverSaved == 1 ? "document needs" : "documents need")} a first Save";
            }
            return text;
        }
    }
}
