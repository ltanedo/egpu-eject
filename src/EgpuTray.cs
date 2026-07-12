using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

[assembly: AssemblyTitle("NVIDIA eGPU Tray")]
[assembly: AssemblyDescription("Monitor and manage an NVIDIA GPU in the supported ASMedia USB4 eGPU dock")]
[assembly: AssemblyCompany("ltanedo")]
[assembly: AssemblyProduct("NVIDIA eGPU Tray")]
[assembly: AssemblyCopyright("Copyright © 2026 ltanedo")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace EgpuTray
{
    internal static class DeviceDetector
    {
        private const uint DIGCF_PRESENT = 0x2;
        private const uint DIGCF_ALLCLASSES = 0x4;

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator,
            IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data,
            StringBuilder id, int size, out int required);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            internal int cbSize;
            internal Guid ClassGuid;
            internal uint DevInst;
            internal IntPtr Reserved;
        }

        internal static bool IsDockPresent()
        {
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, "USB4", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == new IntPtr(-1)) return false;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        continue;
                    }
                    var id = new StringBuilder(512);
                    int required;
                    if (SetupDiGetDeviceInstanceIdW(set, ref data, id, id.Capacity, out required) &&
                        id.ToString().StartsWith("USB4\\VID_174C&PID_2461", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return false;
        }
    }

    internal sealed class DeviceEventForm : Form
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;
        internal event EventHandler DevicesChanged;

        internal DeviceEventForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE && m.WParam.ToInt32() == DBT_DEVNODES_CHANGED && DevicesChanged != null)
                DevicesChanged(this, EventArgs.Empty);
            base.WndProc(ref m);
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private const string StartupTaskName = "NVIDIA eGPU Tray";
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem statusItem = new ToolStripMenuItem("Starting…");
        private readonly ToolStripMenuItem disconnectItem = new ToolStripMenuItem("Disconnect eGPU");
        private readonly ToolStripMenuItem reconnectItem = new ToolStripMenuItem("Reconnect now");
        private readonly ToolStripMenuItem startupItem = new ToolStripMenuItem("Run at startup");
        private readonly DeviceEventForm eventForm = new DeviceEventForm();
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        private bool dockPresent;
        private bool busy;

        internal TrayContext()
        {
            statusItem.Enabled = false;
            disconnectItem.Click += async (s, e) => await Disconnect();
            reconnectItem.Click += async (s, e) => await Reconnect("Manual reconnect");
            startupItem.Checked = StartupTaskExists();
            startupItem.CheckOnClick = false;
            startupItem.Click += (s, e) => ToggleStartup();
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitThread();
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Items.Add(statusItem);
            tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            tray.ContextMenuStrip.Items.Add(disconnectItem);
            tray.ContextMenuStrip.Items.Add(reconnectItem);
            tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            tray.ContextMenuStrip.Items.Add(startupItem);
            tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            tray.ContextMenuStrip.Items.Add(exitItem);
            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Text = "NVIDIA eGPU Tray";
            tray.Visible = true;

            debounce.Interval = 1600;
            debounce.Tick += async (s, e) =>
            {
                debounce.Stop();
                await HandleDeviceChange();
            };
            eventForm.DevicesChanged += (s, e) => { debounce.Stop(); debounce.Start(); };
            IntPtr eventHandle = eventForm.Handle;

            dockPresent = DeviceDetector.IsDockPresent();
            if (dockPresent) eventForm.BeginInvoke(new Action(async () => await Reconnect("Dock detected at startup")));
            else SetStatus("Dock disconnected", false);
        }

        private async Task HandleDeviceChange()
        {
            bool nowPresent = DeviceDetector.IsDockPresent();
            if (nowPresent && !dockPresent)
            {
                dockPresent = true;
                await Reconnect("USB4 dock connected");
            }
            else if (!nowPresent && dockPresent)
            {
                dockPresent = false;
                SetStatus("Dock disconnected", false);
                Notify("eGPU disconnected", "The ASMedia USB4 dock was unplugged.", ToolTipIcon.Info);
            }
        }

        private async Task Reconnect(string reason)
        {
            if (busy) return;
            busy = true;
            UpdateActions();
            SetStatus("Recovering eGPU…", true);
            try
            {
                string result = await Task.Run(() => EgpuReconnect.Reconnector.Run());
                dockPresent = DeviceDetector.IsDockPresent();
                SetStatus("NVIDIA eGPU connected", true);
                Notify("eGPU ready", result, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                dockPresent = DeviceDetector.IsDockPresent();
                SetStatus("Reconnect failed", true);
                Notify(reason + " failed", ex.Message, ToolTipIcon.Error);
            }
            finally { busy = false; UpdateActions(); }
        }

        private async Task Disconnect()
        {
            if (busy) return;
            busy = true;
            UpdateActions();
            SetStatus("Disabling eGPU…", true);
            try
            {
                EgpuEject.EjectResult result = await Task.Run(() => EgpuEject.Ejector.ForceDisconnectRtx());
                if (!result.Success) throw new InvalidOperationException(result.Message);
                SetStatus("Safe to unplug", true);
                Notify("Safe to unplug eGPU", result.Message, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                SetStatus("Disconnect failed", true);
                Notify("eGPU disconnect failed", ex.Message, ToolTipIcon.Error);
            }
            finally { busy = false; UpdateActions(); }
        }

        private void SetStatus(string text, bool present)
        {
            statusItem.Text = text;
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            disconnectItem.Enabled = present && !busy;
            reconnectItem.Enabled = !busy;
        }

        private void UpdateActions()
        {
            disconnectItem.Enabled = dockPresent && !busy;
            reconnectItem.Enabled = !busy;
        }

        private void Notify(string title, string text, ToolTipIcon icon)
        {
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text.Length > 250 ? text.Substring(0, 250) : text;
            tray.BalloonTipIcon = icon;
            tray.ShowBalloonTip(5000);
        }

        private static bool StartupTaskExists()
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\schtasks.exe"),
                Arguments = "/Query /TN \"" + StartupTaskName + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }

        private void ToggleStartup()
        {
            bool enable = !startupItem.Checked;
            string arguments = enable
                ? "/Create /TN \"" + StartupTaskName + "\" /TR \"\\\"" + Application.ExecutablePath + "\\\" --elevated\" /SC ONLOGON /RL HIGHEST /IT /F"
                : "/Delete /TN \"" + StartupTaskName + "\" /F";
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Notify("Startup setting failed", output.Trim(), ToolTipIcon.Error);
                    return;
                }
            }
            startupItem.Checked = enable;
            Notify("Startup setting updated", enable ? "eGPU Tray will run when you sign in." : "eGPU Tray will no longer run automatically.", ToolTipIcon.Info);
        }

        protected override void ExitThreadCore()
        {
            debounce.Stop();
            tray.Visible = false;
            tray.Dispose();
            eventForm.Dispose();
            base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        private static Mutex singleInstance;

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            singleInstance = new Mutex(true, @"Local\NvidiaEgpuTray", out created);
            if (!created) return;
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
            Application.Run(new TrayContext());
        }
    }
}
