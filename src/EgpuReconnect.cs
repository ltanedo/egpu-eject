using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("eGPU Reconnect")]
[assembly: AssemblyDescription("Re-enable the ASMedia bridge for an RTX 4060 Ti eGPU")]
[assembly: AssemblyCompany("ltanedo")]
[assembly: AssemblyProduct("eGPU Reconnect")]
[assembly: AssemblyCopyright("Copyright © 2026 ltanedo")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace EgpuReconnect
{
    internal sealed class CommandResult
    {
        internal int ExitCode;
        internal string Output;
    }

    internal static class Reconnector
    {
        // The downstream ASMedia switch port directly above this machine's RTX 4060 Ti.
        private const string BridgeId = @"PCI\VEN_1B21&DEV_2461&SUBSYS_24611B21&REV_00\5&23E67DFE&0&000021";

        internal static string Run()
        {
            CommandResult enable = Pnp("/enable-device \"" + BridgeId + "\"");
            bool enabled = enable.Output.IndexOf("enabled successfully", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           enable.Output.IndexOf("already enabled", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!enabled)
                throw new InvalidOperationException("Windows could not enable the eGPU bridge.\n\n" + enable.Output.Trim());

            CommandResult scan = Pnp("/scan-devices");
            bool scanned = scan.Output.IndexOf("completed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           scan.ExitCode == 0;
            if (!scanned)
                throw new InvalidOperationException("The bridge was enabled, but hardware scan failed.\n\n" + scan.Output.Trim());

            return "eGPU bridge enabled and hardware scan completed.\n\nThe RTX 4060 Ti and connected TV should now appear.";
        }

        private static CommandResult Pnp(string arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\pnputil.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new CommandResult { ExitCode = process.ExitCode, Output = output };
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label status = new Label();

        internal MainForm()
        {
            Text = "eGPU Reconnect";
            ClientSize = new Size(640, 330);
            MinimumSize = new Size(640, 330);
            BackColor = Color.FromArgb(20, 22, 24);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 14f);

            var title = new Label
            {
                Text = "RTX 4060 Ti eGPU",
                Font = new Font("Segoe UI Semibold", 25f),
                ForeColor = Color.FromArgb(118, 224, 43),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 90
            };
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.Padding = new Padding(45, 10, 45, 10);
            status.Font = new Font("Segoe UI", 16f);
            Controls.Add(status);
            Controls.Add(title);
            Shown += async (s, e) => await Reconnect();
        }

        private async Task Reconnect()
        {
            status.Text = "Re-enabling the eGPU bridge and scanning hardware…";
            UseWaitCursor = true;
            try
            {
                string result = await Task.Run(() => Reconnector.Run());
                UseWaitCursor = false;
                status.ForeColor = Color.FromArgb(118, 224, 43);
                status.Text = result;
                await Task.Delay(5000);
                Close();
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                MessageBox.Show(ex.Message, "eGPU reconnect failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (!elevated)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "--elevated",
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode != 1223)
                        MessageBox.Show(ex.Message, "Could not start as administrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
