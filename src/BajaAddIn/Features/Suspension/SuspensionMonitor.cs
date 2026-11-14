using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;

namespace Lbr.CadTools.AddIn.Features.Suspension
{
    /// <summary>One reading of the suspension.</summary>
    internal sealed class MonitorReading
    {
        public List<CornerGeometry> Corners { get; set; } = new List<CornerGeometry>();

        public SteeringGeometry Steering { get; set; }

        public List<string> Problems { get; set; } = new List<string>();

        public int PointCount { get; set; }

        /// <summary>How long the reading took, for the status line.</summary>
        public TimeSpan Took { get; set; }
    }

    /// <summary>
    /// Live suspension numbers (issue #12). Finds the HP_ points once, then
    /// re-reads only their positions whenever the view repaints (which happens
    /// while dragging), at most every <see cref="MinInterval"/>. A timer catches
    /// the last position after a drag stops and any change made without a repaint.
    /// </summary>
    internal sealed class SuspensionMonitor : IDisposable
    {
        public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(90);

        private readonly IModelDoc2 doc;
        private readonly SuspensionSettings settings;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch sinceUpdate = Stopwatch.StartNew();
        private ModelView view;
        private HardpointReader.Found found;
        private bool pending = true;
        private bool disposed;

        public SuspensionMonitor(IModelDoc2 doc, SuspensionSettings settings)
        {
            this.doc = doc;
            this.settings = settings;
            Title = doc.GetTitle();
            FindPoints();
            timer = new System.Windows.Forms.Timer { Interval = 150 };
            timer.Tick += (s, e) => OnTick();
        }

        /// <summary>New numbers are ready.</summary>
        public event Action<MonitorReading> Updated;

        /// <summary>The document closed or can no longer be read.</summary>
        public event Action Lost;

        public string Title { get; }

        public MonitorReading Last { get; private set; }

        /// <summary>Starts listening to the view and the timer. Out of process (tests) call <see cref="Update"/> instead.</summary>
        public void Start()
        {
            try
            {
                view = doc.ActiveView as ModelView;
                if (view != null)
                {
                    view.RepaintPostNotify += OnRepaint;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Suspension monitor: could not listen to the view; using the timer only", ex);
                view = null;
            }
            timer.Start();
            Update();
        }

        /// <summary>Looks for HP_ points again (after adding or renaming some).</summary>
        public void FindPoints()
        {
            found = HardpointReader.Find(doc);
            pending = true;
        }

        /// <summary>Reads the points and recomputes now.</summary>
        public MonitorReading Update()
        {
            var clock = Stopwatch.StartNew();
            HardpointReader.Result read = HardpointReader.Evaluate(found);
            var problems = new List<string>(read.Problems);
            (List<CornerGeometry> corners, SteeringGeometry steering) = HardpointReader.Compute(read, settings, problems);
            Last = new MonitorReading
            {
                Corners = corners,
                Steering = steering,
                Problems = problems,
                PointCount = read.Points.Count,
                Took = clock.Elapsed,
            };
            pending = false;
            sinceUpdate.Restart();
            Updated?.Invoke(Last);
            return Last;
        }

        private int OnRepaint()
        {
            if (disposed)
            {
                return 0;
            }
            if (sinceUpdate.Elapsed >= MinInterval)
            {
                SafeUpdate();
            }
            else
            {
                pending = true; // the timer picks it up
            }
            return 0;
        }

        private void OnTick()
        {
            if (disposed)
            {
                return;
            }
            // Repaints mark pending; otherwise refresh once a second to catch other changes.
            if (pending || sinceUpdate.Elapsed > TimeSpan.FromSeconds(1))
            {
                SafeUpdate();
            }
        }

        private void SafeUpdate()
        {
            try
            {
                Update();
            }
            catch (Exception ex)
            {
                // The document closed, or SolidWorks is busy in a way that faults; stop quietly.
                Log.Error("Suspension monitor stopped", ex);
                Dispose();
                Lost?.Invoke();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            timer.Stop();
            timer.Dispose();
            if (view != null)
            {
                try
                {
                    view.RepaintPostNotify -= OnRepaint;
                }
                catch (Exception)
                {
                    // The view may already be gone.
                }
                view = null;
            }
        }
    }
}
