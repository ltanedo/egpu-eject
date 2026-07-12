using System;
using System.ComponentModel;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("eGPU Eject")]
[assembly: AssemblyDescription("Safely eject an NVIDIA GeForce RTX 4060 Ti eGPU")]
[assembly: AssemblyCompany("ltanedo")]
[assembly: AssemblyProduct("eGPU Eject")]
[assembly: AssemblyCopyright("Copyright © 2026 ltanedo")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]

namespace EgpuEject
{
    internal static class Native
    {
        internal const uint CR_SUCCESS = 0;
        internal const uint CM_LOCATE_DEVNODE_NORMAL = 0;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint CM_Request_Device_EjectW(uint devInst, out PnpVetoType vetoType,
            StringBuilder vetoName, int nameLength, uint flags);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_Child(out uint child, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_Sibling(out uint sibling, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, uint flags);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Disable_DevNode(uint devInst, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string enumerator,
            IntPtr hwndParent, uint flags);

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

        internal const uint DIGCF_PRESENT = 0x2;
        internal const uint SPDRP_DEVICEDESC = 0;
        internal static readonly Guid DisplayClass = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            internal int cbSize;
            internal Guid ClassGuid;
            internal uint DevInst;
            internal IntPtr Reserved;
        }

        internal enum PnpVetoType
        {
            Unknown, LegacyDevice, PendingClose, WindowsApp, WindowsService, OutstandingOpen,
            Device, Driver, IllegalDeviceRequest, InsufficientPower, NonDisableable, LegacyDriver,
            InsufficientRights, AlreadyRemoved
        }
    }

    internal sealed class EjectResult
    {
        internal bool Success;
        internal string Message;
    }

    internal static class Ejector
    {
        internal static EjectResult Eject4060Ti()
        {
            string id = FindGpu();
            if (id == null)
                return new EjectResult { Message = "RTX 4060 Ti eGPU not found.\n\nConnect it and choose Retry." };

            uint devInst;
            uint locate = Native.CM_Locate_DevNodeW(out devInst, id, Native.CM_LOCATE_DEVNODE_NORMAL);
            if (locate != Native.CR_SUCCESS)
                return new EjectResult { Message = "Windows could not open the eGPU device (error " + locate + ")." };

            var vetoName = new StringBuilder(260);
            Native.PnpVetoType veto;
            uint result = Native.CM_Request_Device_EjectW(devInst, out veto, vetoName, vetoName.Capacity, 0);
            if (result == Native.CR_SUCCESS)
                return new EjectResult { Success = true, Message = "Safe to unplug the RTX 4060 Ti eGPU." };

            string blocker = vetoName.Length > 0 ? "\n\nBlocked by: " + vetoName : "";
            return new EjectResult
            {
                Message = "The eGPU is still in use, so Windows did not eject it.\n\nClose games and GPU apps, then choose Retry.\n\nReason: " + veto + blocker
            };
        }

        internal static EjectResult ForceDisconnect4060Ti()
        {
            string id = FindGpu();
            if (id == null)
                return new EjectResult { Message = "RTX 4060 Ti eGPU not found." };

            uint gpu, bridge;
            uint result = Native.CM_Locate_DevNodeW(out gpu, id, Native.CM_LOCATE_DEVNODE_NORMAL);
            if (result != Native.CR_SUCCESS || Native.CM_Get_Parent(out bridge, gpu, 0) != Native.CR_SUCCESS)
                return new EjectResult { Message = "Windows could not locate the eGPU bridge." };

            uint child;
            if (Native.CM_Get_Child(out child, bridge, 0) != Native.CR_SUCCESS)
                return new EjectResult { Message = "Windows could not enumerate the eGPU functions." };

            string gpuId = id;
            string audioId = null;
            uint audioDevInst = 0;
            do
            {
                var childId = new StringBuilder(512);
                if (Native.CM_Get_Device_IDW(child, childId, childId.Capacity, 0) == Native.CR_SUCCESS)
                {
                    string value = childId.ToString();
                    if (!value.Equals(gpuId, StringComparison.OrdinalIgnoreCase) &&
                        value.StartsWith("PCI\\VEN_10DE", StringComparison.OrdinalIgnoreCase))
                    {
                        audioId = value;
                        audioDevInst = child;
                    }
                }
                uint sibling;
                if (Native.CM_Get_Sibling(out sibling, child, 0) != Native.CR_SUCCESS) break;
                child = sibling;
            } while (true);

            // Disable the removable child functions directly and non-persistently. Disabling the
            // system PCI bridge or force-removing the devnodes is deferred until reboot on this
            // USB4/NVIDIA stack. No CM_DISABLE_PERSIST flag is used, so a later enumeration can
            // bring the devices back normally. Display goes first: active HDMI audio commonly
            // vetoes its own disable until the display adapter has been taken offline.
            uint displayResult = Native.CM_Disable_DevNode(gpu, 0);
            if (displayResult != Native.CR_SUCCESS)
                return new EjectResult { Message = "Windows could not disable the RTX 4060 Ti (Configuration Manager error " + displayResult + ")." };

            uint audioResult = Native.CR_SUCCESS;
            if (audioId != null)
                audioResult = Native.CM_Disable_DevNode(audioDevInst, 0);

            string audioNote = audioResult == Native.CR_SUCCESS
                ? "Display and HDMI-audio functions are offline."
                : "The display function is offline. HDMI audio remained vetoed (error " + audioResult + "), but it is safe to unplug with the GPU driver stopped.";
            return new EjectResult { Success = true, Message = audioNote + "\n\nIt is now safe to unplug the cable." };
        }

        private static string FindGpu()
        {
            Guid display = Native.DisplayClass;
            IntPtr set = Native.SetupDiGetClassDevsW(ref display, null, IntPtr.Zero, Native.DIGCF_PRESENT);
            if (set == new IntPtr(-1)) return null;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var data = new Native.SP_DEVINFO_DATA();
                    data.cbSize = Marshal.SizeOf(data);
                    if (!Native.SetupDiEnumDeviceInfo(set, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        continue;
                    }

                    var raw = new byte[1024];
                    uint type, needed;
                    if (!Native.SetupDiGetDeviceRegistryPropertyW(set, ref data, Native.SPDRP_DEVICEDESC,
                        out type, raw, (uint)raw.Length, out needed)) continue;
                    string name = Encoding.Unicode.GetString(raw).TrimEnd('\0');
                    if (name.IndexOf("NVIDIA GeForce RTX 4060 Ti", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var id = new StringBuilder(512);
                    int required;
                    if (Native.SetupDiGetDeviceInstanceIdW(set, ref data, id, id.Capacity, out required))
                        return id.ToString();
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return null;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label status = new Label();
        private readonly Button retry = new Button();
        private readonly Button force = new Button();
        private readonly bool forceMode;

        internal MainForm(bool forceMode)
        {
            this.forceMode = forceMode;
            Text = "eGPU Eject";
            ClientSize = new Size(640, 360);
            MinimumSize = new Size(640, 360);
            BackColor = Color.FromArgb(20, 22, 24);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 14f);
            KeyPreview = true;

            var title = new Label { Text = "RTX 4060 Ti eGPU", Font = new Font("Segoe UI Semibold", 25f),
                ForeColor = Color.FromArgb(118, 224, 43), TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top, Height = 82 };
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.Padding = new Padding(45, 10, 45, 10);
            status.Font = new Font("Segoe UI", 16f);
            retry.Text = "Retry";
            retry.Height = 72;
            retry.Dock = DockStyle.Bottom;
            retry.FlatStyle = FlatStyle.Flat;
            retry.FlatAppearance.BorderSize = 0;
            retry.BackColor = Color.FromArgb(83, 160, 28);
            retry.ForeColor = Color.White;
            retry.Visible = false;
            retry.Click += async (s, e) => await Attempt();
            force.Text = "Force disable eGPU (Admin)";
            force.Height = 64;
            force.Dock = DockStyle.Bottom;
            force.FlatStyle = FlatStyle.Flat;
            force.FlatAppearance.BorderColor = Color.FromArgb(185, 80, 55);
            force.BackColor = Color.FromArgb(75, 35, 30);
            force.ForeColor = Color.White;
            force.Visible = false;
            force.Click += (s, e) => StartElevatedForce();
            Controls.Add(status);
            Controls.Add(force);
            Controls.Add(retry);
            Controls.Add(title);
            Shown += async (s, e) => { if (forceMode) await AttemptForce(); else await Attempt(); };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.B) Close(); };
        }

        private async Task Attempt()
        {
            retry.Visible = false;
            status.Text = "Asking Windows to safely eject the eGPU…";
            UseWaitCursor = true;
            EjectResult result = await Task.Run(() => Ejector.Eject4060Ti());
            UseWaitCursor = false;
            status.Text = result.Message;
            if (result.Success)
            {
                status.ForeColor = Color.FromArgb(118, 224, 43);
                await Task.Delay(3500);
                Close();
            }
            else
            {
                status.ForeColor = Color.White;
                retry.Visible = true;
                force.Visible = true;
                retry.Focus();
            }
        }

        private void StartElevatedForce()
        {
            DialogResult choice = MessageBox.Show(
                "Force disable stops the eGPU's display and audio devices even if normal eject is blocked.\n\n" +
                "Displays connected to the eGPU will go black immediately. Unsaved GPU work may be lost.\n\nContinue?",
                "Force disconnect eGPU", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--force",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Close();
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223)
                    MessageBox.Show(ex.Message, "Could not start as administrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task AttemptForce()
        {
            retry.Visible = false;
            force.Visible = false;
            status.Text = "Disabling the eGPU devices…";
            UseWaitCursor = true;
            EjectResult result = await Task.Run(() => Ejector.ForceDisconnect4060Ti());
            UseWaitCursor = false;
            status.Text = result.Message;
            status.ForeColor = result.Success ? Color.FromArgb(118, 224, 43) : Color.White;
            if (result.Success) { await Task.Delay(5000); Close(); }
            else
            {
                MessageBox.Show(result.Message, "Force disconnect failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            bool force = args.Length > 0 && string.Equals(args[0], "--force", StringComparison.OrdinalIgnoreCase);
            Application.Run(new MainForm(force));
        }
    }
}
