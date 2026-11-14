using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Util;

namespace Lbr.CadTools.Setup
{
    /// <summary>
    /// The installer window: header, what is installed, Install / Uninstall,
    /// a step list, and the signature footer. Built in code.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Muted = Color.FromArgb(0x6B, 0x75, 0x7D);
        private static readonly Color Good = Color.FromArgb(0x2E, 0x7D, 0x32);
        private static readonly Color Bad = Color.FromArgb(0xC6, 0x28, 0x28);

        private readonly Label status = new Label();
        private readonly Label detail = new Label();
        private readonly TextBox steps = new TextBox();
        private readonly Button install = new Button();
        private readonly Button uninstall = new Button();
        private readonly Button close = new Button();
        private readonly bool uninstallMode;

        public SetupForm(bool uninstallMode)
        {
            this.uninstallMode = uninstallMode;
            string version = Installer.PayloadVersion.ToString(3);
            Text = "LBR CAD Tools Setup";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            ClientSize = new Size(540, 390);
            using (Bitmap icon = Brand.ProductIcon(32))
            {
                Icon = Icon.FromHandle(icon.GetHicon());
            }

            var header = new Panel { Dock = DockStyle.Top, Height = Brand.HeaderHeight };
            header.Paint += (s, e) => Brand.PaintHeader(e.Graphics, header.ClientRectangle, Brand.Product, $"Setup  -  version {version}");

            status.Location = new Point(22, Brand.HeaderHeight + 18);
            status.Size = new Size(496, 24);
            status.Font = new Font("Segoe UI Semibold", 11f);
            status.ForeColor = Brand.Ink;

            detail.Location = new Point(22, Brand.HeaderHeight + 46);
            detail.Size = new Size(496, 58);
            detail.ForeColor = Muted;

            steps.Location = new Point(22, Brand.HeaderHeight + 108);
            steps.Size = new Size(496, 92);
            steps.Multiline = true;
            steps.ReadOnly = true;
            steps.BorderStyle = BorderStyle.None;
            steps.BackColor = Color.FromArgb(0xF6, 0xF7, 0xF8);
            steps.ForeColor = Brand.Ink;
            steps.Font = new Font("Segoe UI", 9f);
            steps.TabStop = false;

            install.AutoSize = true;
            install.MinimumSize = new Size(110, 32);
            install.BackColor = Brand.BurntOrange;
            install.ForeColor = Color.White;
            install.FlatStyle = FlatStyle.Flat;
            install.FlatAppearance.BorderSize = 0;
            install.Font = new Font("Segoe UI Semibold", 9.5f);
            install.Click += async (s, e) => await Run(isInstall: true);

            uninstall.Text = "Uninstall";
            uninstall.AutoSize = true;
            uninstall.MinimumSize = new Size(96, 32);
            uninstall.FlatStyle = FlatStyle.Flat;
            uninstall.FlatAppearance.BorderColor = Color.FromArgb(0xC9, 0xCE, 0xD2);
            uninstall.Click += async (s, e) => await Run(isInstall: false);

            close.Text = "Close";
            close.AutoSize = true;
            close.MinimumSize = new Size(86, 32);
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderColor = Color.FromArgb(0xC9, 0xCE, 0xD2);
            close.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Location = new Point(12, Brand.HeaderHeight + 210),
                Size = new Size(516, 42),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(uninstall);
            buttons.Controls.Add(install);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 36 };
            footer.Paint += (s, e) =>
            {
                using (var line = new Pen(Color.FromArgb(0xE3, 0xE6, 0xE8)))
                {
                    e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
                }
                Brand.PaintSignature(e.Graphics, new Point(18, 9), BackColor);
            };
            var link = new LinkLabel
            {
                Text = "github.com/longhorn-baja-racing/lbr-cad-tools",
                AutoSize = true,
                LinkColor = Muted,
                ActiveLinkColor = Brand.BurntOrange,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
            };
            link.LinkClicked += (s, e) => Process.Start(Brand.RepositoryUrl);
            footer.Controls.Add(link);
            footer.Layout += (s, e) => link.Location = new Point(footer.Width - link.Width - 16, 10);

            Controls.Add(status);
            Controls.Add(detail);
            Controls.Add(steps);
            Controls.Add(buttons);
            Controls.Add(footer);
            Controls.Add(header);
            AcceptButton = install;
            CancelButton = close;

            ShowState();
        }

        private void ShowState()
        {
            Version installed = Installer.InstalledVersion();
            Version mine = Installer.PayloadVersion;
            uninstall.Visible = installed != null;

            if (installed == null)
            {
                status.Text = "Ready to install";
                install.Text = "Install";
            }
            else if (installed.Major == 0 && installed.Minor == 0)
            {
                status.Text = "An earlier version is installed";
                install.Text = "Update";
            }
            else
            {
                int cmp = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build))
                    .CompareTo(new Version(mine.Major, mine.Minor, Math.Max(0, mine.Build)));
                status.Text = cmp < 0 ? $"Version {installed.ToString(3)} is installed"
                    : cmp == 0 ? "This version is installed" : $"A newer version ({installed.ToString(3)}) is installed";
                install.Text = cmp < 0 ? "Update" : "Reinstall";
            }

            detail.Text = "Adds the LBR CAD Tools tab to SolidWorks in parts, assemblies, and drawings.\n" +
                          $"Installs to {Installer.InstallDir}. Your settings are kept.";
            int? have = Installer.NewestSolidWorksYear();
            if (have == null)
            {
                detail.Text += "\nNo SolidWorks installation was found on this computer.";
            }
            else if (have < Installer.MinimumYear)
            {
                detail.Text += $"\nNeeds SolidWorks {Installer.MinimumYear} or newer; this computer has {have}. Some tools may not work.";
            }

            steps.ForeColor = Muted;
            steps.Text = "Close SolidWorks before you continue.";

            if (uninstallMode && installed != null)
            {
                status.Text = "Remove LBR CAD Tools?";
                AcceptButton = uninstall;
                ActiveControl = uninstall;
            }
        }

        private void Step(string text, Color? color = null)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Step(text, color)));
                return;
            }
            steps.AppendText((steps.TextLength > 0 ? "\r\n" : "") + text);
            if (color.HasValue)
            {
                steps.ForeColor = color.Value;
            }
        }

        private async Task Run(bool isInstall)
        {
            install.Enabled = uninstall.Enabled = close.Enabled = false;
            steps.Clear();
            steps.ForeColor = Brand.Ink;
            UseWaitCursor = true;
            try
            {
                await Task.Run(() =>
                {
                    if (isInstall)
                    {
                        Installer.Install(s => Step("-  " + s));
                    }
                    else
                    {
                        Installer.Uninstall(s => Step("-  " + s));
                    }
                });

                if (isInstall)
                {
                    Step("Done. Start SolidWorks and open a part, assembly, or drawing: the LBR CAD Tools tab is at the top.", Good);
                    status.Text = "Installed";
                    status.ForeColor = Good;
                }
                else
                {
                    Step("Done. LBR CAD Tools was removed. Your settings stay in %LOCALAPPDATA%\\LBR CAD Tools.", Good);
                    status.Text = "Uninstalled";
                    status.ForeColor = Good;
                }
                install.Visible = isInstall;
                uninstall.Visible = false;
                install.Enabled = false;
                close.Text = "Done";
                AcceptButton = close;
            }
            catch (Exception ex)
            {
                Step(ex.Message, Bad);
                status.Text = isInstall ? "Install did not finish" : "Uninstall did not finish";
                status.ForeColor = Bad;
                install.Enabled = uninstall.Enabled = true;
            }
            finally
            {
                UseWaitCursor = false;
                close.Enabled = true;
            }
        }
    }
}
