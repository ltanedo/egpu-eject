using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

#if !TRAY_BUILD
[assembly: AssemblyTitle("eGPU Reconnect")]
[assembly: AssemblyDescription("Re-enable the ASMedia bridge for an NVIDIA eGPU")]
[assembly: AssemblyCompany("ltanedo")]
[assembly: AssemblyProduct("eGPU Reconnect")]
[assembly: AssemblyCopyright("Copyright © 2026 ltanedo")]
[assembly: AssemblyVersion("1.6.0.0")]
[assembly: AssemblyFileVersion("1.6.0.0")]
#endif

namespace EgpuReconnect
{
    internal static class Native
    {
        internal const uint CR_SUCCESS = 0;
        internal const uint DIGCF_PRESENT = 0x2;
        internal const uint SPDRP_DEVICEDESC = 0;
        internal const uint CM_PROB_FAILED_POST_START = 43;
        internal static readonly Guid DisplayClass = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data,
            StringBuilder id, int size, out int required);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data,
            uint property, out uint propertyType, byte[] buffer, uint size, out uint required);

        [DllImport("setupapi.dll")]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_Child(out uint child, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_Sibling(out uint sibling, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, uint flags);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            internal int cbSize;
            internal Guid ClassGuid;
            internal uint DevInst;
            internal IntPtr Reserved;
        }
    }

    internal sealed class NvidiaEgpu
    {
        internal string Id;
        internal string Name;
        internal string ParentId;
        internal uint DevInst;
    }

    internal sealed class CommandResult
    {
        internal int ExitCode;
        internal string Output;
    }

    internal static class Reconnector
    {
        // Compatible ID shared by the ASMedia PCIe switch ports in this eGPU dock.
        // Using the hardware ID rather than an instance path survives GPU swaps and port changes.
        private const string BridgeHardwareId = @"PCI\VEN_1B21&DEV_2461";
        private const string Usb4RouterHardwareId = @"USB4\VID_174C&PID_2461";

        internal static string Run()
        {
            CommandResult enable = Pnp("/enable-device /deviceid \"" + BridgeHardwareId + "\"");
            bool enabled = enable.Output.IndexOf("enabled successfully", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           enable.Output.IndexOf("already enabled", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!enabled)
                throw new InvalidOperationException("Windows could not enable the eGPU bridge.\n\n" + enable.Output.Trim());

            CommandResult scan = Pnp("/scan-devices");
            bool scanned = scan.Output.IndexOf("completed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           scan.ExitCode == 0;
            if (!scanned)
                throw new InvalidOperationException("The bridge was enabled, but hardware scan failed.\n\n" + scan.Output.Trim());

            NvidiaEgpu gpu = FindNvidiaEgpu();
            if (gpu == null)
                throw new InvalidOperationException("The dock bridge is enabled, but no NVIDIA display adapter was found behind it.");

            EnableDockNvidiaFunctions(gpu.ParentId);
            Pnp("/scan-devices");
            Thread.Sleep(2500);
            gpu = FindNvidiaEgpu();
            if (gpu == null)
                throw new InvalidOperationException("The NVIDIA dock devices were enabled, but the display adapter did not reappear.");

            uint problem = GetProblem(gpu.DevInst);
            if (problem == Native.CM_PROB_FAILED_POST_START)
            {
                Pnp("/restart-device \"" + gpu.Id + "\"");
                Thread.Sleep(2500);
                gpu = FindNvidiaEgpu();
                problem = gpu == null ? 45u : GetProblem(gpu.DevInst);
            }

            if (problem == Native.CM_PROB_FAILED_POST_START)
            {
                Pnp("/disable-device \"" + gpu.Id + "\"");
                Thread.Sleep(750);
                Pnp("/enable-device \"" + gpu.Id + "\"");
                Pnp("/scan-devices");
                Thread.Sleep(3000);
                gpu = FindNvidiaEgpu();
                problem = gpu == null ? 45u : GetProblem(gpu.DevInst);
            }

            if (problem == Native.CM_PROB_FAILED_POST_START)
            {
                string bridgeId = gpu.ParentId;
                Pnp("/restart-device \"" + bridgeId + "\"");
                Thread.Sleep(2000);
                Pnp("/scan-devices");
                Thread.Sleep(4000);
                gpu = FindNvidiaEgpu();
                problem = gpu == null ? 45u : GetProblem(gpu.DevInst);
            }

            if (problem == Native.CM_PROB_FAILED_POST_START)
            {
                // This is the closest software equivalent to unplugging and reconnecting the
                // dock cable: restart only the ASMedia USB4 router belonging to this enclosure,
                // then allow the PCIe tunnel and its child devices to enumerate from scratch.
                Pnp("/restart-device /deviceid \"" + Usb4RouterHardwareId + "\"");
                Thread.Sleep(5000);
                Pnp("/scan-devices");
                Thread.Sleep(7000);
                gpu = FindNvidiaEgpu();
                problem = gpu == null ? 45u : GetProblem(gpu.DevInst);
            }

            if (problem == Native.CM_PROB_FAILED_POST_START)
                throw new InvalidOperationException(gpu.Name + " still reports Code 43 after GPU restart, disable/enable, PCIe bridge reset, and ASMedia USB4 router restart.\n\nPhysically unplug and reconnect the USB4 cable. If that does not work, fully shut down the PC and dock, remove dock power for 30 seconds, then reconnect.");
            if (gpu == null)
                throw new InvalidOperationException("The ASMedia USB4 router restarted, but the NVIDIA eGPU did not reappear.\n\nPhysically reconnect the USB4 cable once.");
            if (problem != 0)
                throw new InvalidOperationException(gpu.Name + " was detected, but Windows reports device problem code " + problem + ".");

            return "eGPU bridge enabled and hardware scan completed.\n\n" + gpu.Name + " is working with no device error.";
        }

        private static void EnableDockNvidiaFunctions(string bridgeId)
        {
            uint bridge;
            if (Native.CM_Locate_DevNodeW(out bridge, bridgeId, 0) != Native.CR_SUCCESS) return;
            uint child;
            if (Native.CM_Get_Child(out child, bridge, 0) != Native.CR_SUCCESS) return;
            do
            {
                var id = new StringBuilder(512);
                if (Native.CM_Get_Device_IDW(child, id, id.Capacity, 0) == Native.CR_SUCCESS &&
                    id.ToString().StartsWith("PCI\\VEN_10DE&", StringComparison.OrdinalIgnoreCase))
                    Pnp("/enable-device \"" + id + "\"");

                uint sibling;
                if (Native.CM_Get_Sibling(out sibling, child, 0) != Native.CR_SUCCESS) break;
                child = sibling;
            } while (true);
        }

        internal static uint GetProblem(uint devInst)
        {
            uint status, problem;
            return Native.CM_Get_DevNode_Status(out status, out problem, devInst, 0) == Native.CR_SUCCESS ? problem : UInt32.MaxValue;
        }

        internal static NvidiaEgpu FindNvidiaEgpu()
        {
            Guid display = Native.DisplayClass;
            IntPtr set = Native.SetupDiGetClassDevsW(ref display, null, IntPtr.Zero, Native.DIGCF_PRESENT);
            if (set == new IntPtr(-1)) return null;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var data = new Native.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(Native.SP_DEVINFO_DATA)) };
                    if (!Native.SetupDiEnumDeviceInfo(set, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        continue;
                    }

                    var id = new StringBuilder(512);
                    int required;
                    if (!Native.SetupDiGetDeviceInstanceIdW(set, ref data, id, id.Capacity, out required) ||
                        !id.ToString().StartsWith("PCI\\VEN_10DE&", StringComparison.OrdinalIgnoreCase)) continue;

                    uint parent;
                    if (Native.CM_Get_Parent(out parent, data.DevInst, 0) != Native.CR_SUCCESS) continue;
                    var parentId = new StringBuilder(512);
                    if (Native.CM_Get_Device_IDW(parent, parentId, parentId.Capacity, 0) != Native.CR_SUCCESS ||
                        !parentId.ToString().StartsWith("PCI\\VEN_1B21&DEV_2461", StringComparison.OrdinalIgnoreCase)) continue;

                    var raw = new byte[1024];
                    uint type, needed;
                    string name = "NVIDIA eGPU";
                    if (Native.SetupDiGetDeviceRegistryPropertyW(set, ref data, Native.SPDRP_DEVICEDESC,
                        out type, raw, (uint)raw.Length, out needed))
                    {
                        int byteCount = (int)Math.Min(needed, (uint)raw.Length);
                        name = Encoding.Unicode.GetString(raw, 0, byteCount);
                        int nullIndex = name.IndexOf('\0');
                        if (nullIndex >= 0) name = name.Substring(0, nullIndex);
                    }
                    return new NvidiaEgpu { Id = id.ToString(), Name = name, ParentId = parentId.ToString(), DevInst = data.DevInst };
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return null;
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
                Text = "NVIDIA eGPU",
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
