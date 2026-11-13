using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.AutoSave
{
    /// <summary>
    /// Saves modified documents on a timer. SolidWorks' own Auto-recover only
    /// writes recovery copies; this saves the real files.
    ///
    /// The timer is a WinForms timer, so ticks run on the SolidWorks UI thread
    /// between user actions, never in the middle of an API call. A tick is
    /// skipped (and retried a minute later) while a command or PropertyManager
    /// page is open.
    /// </summary>
    internal sealed class AutoSaver : IDisposable
    {
        private const int RetryMilliseconds = 60 * 1000;

        private readonly ISldWorks app;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private int intervalMilliseconds;

        internal sealed class Result
        {
            public int Saved { get; set; }
            public List<string> Failed { get; } = new List<string>();
            public int NeverSaved { get; set; }
        }

        public AutoSaver(ISldWorks app)
        {
            this.app = app;
            timer.Tick += (s, e) => OnTick();
        }

        public bool Running => timer.Enabled;

        /// <summary>Last status bar text, shown in the settings dialog.</summary>
        public string LastStatus { get; private set; }

        public void Apply(AutoSaveSettings settings)
        {
            timer.Stop();
            intervalMilliseconds = settings.IntervalMinutes * 60 * 1000;
            if (settings.Enabled)
            {
                timer.Interval = intervalMilliseconds;
                timer.Start();
            }
            Log.Info($"Auto-save: {(settings.Enabled ? $"every {settings.IntervalMinutes} min" : "off")}");
        }

        private void OnTick()
        {
            timer.Stop();
            try
            {
                if (IsBusy(app, out string busyWith))
                {
                    Log.Info($"Auto-save: postponed, busy with {busyWith}");
                    timer.Interval = RetryMilliseconds;
                    return;
                }
                timer.Interval = intervalMilliseconds;

                Result r = SaveNow(app);
                LastStatus = AutoSavePolicy.Status(DateTime.Now, r.Saved, r.Failed, r.NeverSaved);
                if (app.Frame() is IFrame frame)
                {
                    frame.SetStatusBarText(LastStatus);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Auto-save tick failed", ex);
            }
            finally
            {
                timer.Start();
            }
        }

        /// <summary>
        /// True while a command or PropertyManager page is open, or a sketch is
        /// being edited. Idle reports command id -3 and no title (SW 2026 SP5);
        /// an open Fillet page reports id 9, title "Fillet". Editing a sketch
        /// reports idle, so it is checked separately.
        /// </summary>
        public static bool IsBusy(ISldWorks app, out string what)
        {
            app.GetRunningCommandInfo(out int commandId, out string title, out bool uiActive);
            if (commandId > 0 || !string.IsNullOrEmpty(title))
            {
                what = string.IsNullOrEmpty(title) ? $"command {commandId}" : title;
                return true;
            }
            if (app.ActiveDoc is IModelDoc2 doc && doc.SketchManager?.ActiveSketch != null)
            {
                what = "sketch editing";
                return true;
            }
            what = null;
            return false;
        }

        /// <summary>Saves every document <see cref="AutoSavePolicy"/> picks.</summary>
        public static Result SaveNow(ISldWorks app)
        {
            List<IModelDoc2> docs = ((app.GetDocuments() as object[]) ?? new object[0]).Cast<IModelDoc2>().ToList();
            List<OpenDocInfo> infos = docs
                .Select(d => new OpenDocInfo(d.GetPathName(), d.Visible, d.IsOpenedReadOnly(), d.GetSaveFlag()))
                .ToList();

            var result = new Result { NeverSaved = AutoSavePolicy.NeverSaved(infos) };
            foreach (int i in AutoSavePolicy.ToSave(infos))
            {
                IModelDoc2 doc = docs[i];
                int errors = 0, warnings = 0;
                bool ok = doc.Save3(
                    (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_SaveReferenced),
                    ref errors, ref warnings);
                if (ok)
                {
                    result.Saved++;
                    Log.Info($"Auto-save: saved {infos[i].Path}");
                }
                else
                {
                    result.Failed.Add(Path.GetFileName(infos[i].Path));
                    Log.Info($"Auto-save: FAILED {infos[i].Path} (errors {errors}, warnings {warnings})");
                }
            }
            return result;
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }
    }
}
