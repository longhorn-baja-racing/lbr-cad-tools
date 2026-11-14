using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.Naming
{
    /// <summary>
    /// Collects the file names Name Check looks at (issue #6): the open
    /// document, every file an assembly uses at any level, drawings in its
    /// folder that belong to those files, and every SolidWorks file name in
    /// the folder tree (for the next free numbers). Reads names only; never
    /// renames or opens anything.
    /// </summary>
    internal static class NameScanner
    {
        /// <summary>Folder files read for numbering; Box folders can be large.</summary>
        private const int MaxFolderFiles = 20000;

        internal sealed class Result
        {
            public NamedFile Top { get; set; }

            public string TopPath { get; set; }

            /// <summary>Files used by the document (and their drawings), without the document itself.</summary>
            public List<NamedFile> Files { get; } = new List<NamedFile>();

            /// <summary>Full path per base name, for the report.</summary>
            public Dictionary<string, string> Paths { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Components that have no file of their own (virtual), by name.</summary>
            public List<string> Virtual { get; } = new List<string>();

            /// <summary>Every SolidWorks file name seen, for <see cref="NamingConvention.NextNumber"/>.</summary>
            public List<string> AllNames { get; } = new List<string>();

            /// <summary>True when the folder had more files than were read.</summary>
            public bool FolderTruncated { get; set; }
        }

        public static CadFileKind KindOf(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToUpperInvariant();
            return ext == ".SLDASM" ? CadFileKind.Assembly : ext == ".SLDDRW" ? CadFileKind.Drawing : CadFileKind.Part;
        }

        public static Result Scan(IModelDoc2 doc)
        {
            var r = new Result();
            string topPath = doc.GetPathName();
            r.TopPath = topPath;
            r.Top = new NamedFile(Path.GetFileNameWithoutExtension(topPath), KindOf(topPath));
            r.Paths[r.Top.BaseName] = topPath;
            r.AllNames.Add(r.Top.BaseName);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { topPath };
            if (doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                object[] components = ((IAssemblyDoc)doc).GetComponents(false) as object[] ?? new object[0];
                foreach (IComponent2 c in components.OfType<IComponent2>())
                {
                    if (c.IsVirtual)
                    {
                        r.Virtual.Add(c.Name2);
                        continue;
                    }
                    string path = c.GetPathName();
                    if (string.IsNullOrEmpty(path) || !seen.Add(path))
                    {
                        continue;
                    }
                    Add(r, path);
                }
            }

            ScanFolder(r, Path.GetDirectoryName(topPath), seen);
            return r;
        }

        private static void Add(Result r, string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            r.Files.Add(new NamedFile(name, KindOf(path)));
            r.Paths[name] = path;
            r.AllNames.Add(name);
        }

        /// <summary>
        /// Reads SolidWorks file names under the document's folder: all of them
        /// count for numbering, and drawings of the checked files join the check.
        /// </summary>
        private static void ScanFolder(Result r, string folder, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }
            // Drawings join the check only for the same number and REV as a file in use;
            // drawings of older revisions kept in the folder are not errors.
            var keys = new HashSet<string>(
                new[] { r.Top }.Concat(r.Files).Select(f => DrawingKey(NamingConvention.Parse(f.BaseName))).Where(k => k != null),
                StringComparer.Ordinal);
            int count = 0;
            foreach (string path in SafeFiles(folder))
            {
                string ext = Path.GetExtension(path).ToUpperInvariant();
                if (ext != ".SLDPRT" && ext != ".SLDASM" && ext != ".SLDDRW")
                {
                    continue;
                }
                if (++count > MaxFolderFiles)
                {
                    r.FolderTruncated = true;
                    return;
                }
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("~$", StringComparison.Ordinal))
                {
                    continue; // SolidWorks lock files
                }
                r.AllNames.Add(name);
                if (ext == ".SLDDRW" && seen.Add(path) && NamingConvention.Parse(name) is CadName parsed && keys.Contains(DrawingKey(parsed)))
                {
                    Add(r, path);
                }
            }
        }

        private static string DrawingKey(CadName n) => n == null ? null : n.Key + "_" + n.RevisionText;

        /// <summary>Files under <paramref name="root"/>, skipping folders that cannot be read.</summary>
        private static IEnumerable<string> SafeFiles(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                string[] files, dirs;
                try
                {
                    files = Directory.GetFiles(dir);
                    dirs = Directory.GetDirectories(dir);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (string f in files)
                {
                    yield return f;
                }
                foreach (string d in dirs)
                {
                    pending.Push(d);
                }
            }
        }
    }
}
