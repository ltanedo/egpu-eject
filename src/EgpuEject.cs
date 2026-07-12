using System;
using System.ComponentModel;
using System.Drawing;
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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

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

        internal MainForm()
        {
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
            Controls.Add(status);
            Controls.Add(retry);
            Controls.Add(title);
            Shown += async (s, e) => await Attempt();
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
                retry.Focus();
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
