# NVIDIA eGPU Utilities for Windows

Controller-friendly Windows tools for disconnecting, reconnecting, and monitoring an NVIDIA GPU in a supported ASMedia USB4 eGPU enclosure.

> [!IMPORTANT]
> This project is currently tailored to enclosures using the ASMedia USB4 router `VID_174C&PID_2461` and PCIe switch `VEN_1B21&DEV_2461`. Check [Compatibility](#compatibility) before using it with another enclosure.

## Download

Download the latest executables from [GitHub Releases](https://github.com/ltanedo/egpu-eject/releases/latest). No installer is required.

| Application | Purpose |
| --- | --- |
| `eGPU-Tray.exe` | Recommended. Combines monitoring, disconnect, reconnect, and recovery in one tray application. |
| `eGPU-Eject.exe` | One-shot, controller-friendly eGPU disconnect utility. |
| `eGPU-Reconnect.exe` | One-shot dock reconnect and NVIDIA device recovery utility. |

All executables are portable, unsigned Windows desktop applications. Windows SmartScreen may show an unknown-publisher warning.

## Tray application

Launch `eGPU-Tray.exe` and approve the administrator prompt. The tray menu provides:

- **Disconnect eGPU** — takes the docked NVIDIA GPU offline before you unplug the cable.
- **Reconnect now** — restores disabled NVIDIA functions and runs dock/GPU recovery.
- **Run at startup** — creates or removes a per-user Task Scheduler logon task.
- **Exit** — stops device monitoring and closes the tray application.

While running, the tray app watches Windows Plug and Play events for the enclosure's ASMedia USB4 router. When the dock appears, it automatically runs the reconnect workflow. Multiple events are debounced, and single-instance protection prevents duplicate tray monitors.

Startup is opt-in. When enabled, the scheduled task runs at highest privilege so automatic launches do not produce a UAC prompt at every sign-in.

## Standalone utilities

### Disconnect

`eGPU-Eject.exe` immediately requests administrator approval and disables the docked NVIDIA display adapter. It disables the GPU before making a best-effort attempt to disable sibling NVIDIA functions such as HDMI audio.

1. Save your work and close games or GPU-intensive applications.
2. Run `eGPU-Eject.exe` and approve the administrator prompt.
3. Wait until the app says it is safe to unplug.
4. Disconnect the USB4 cable.

Displays connected through the eGPU can go black as soon as the GPU is disabled.

### Reconnect

`eGPU-Reconnect.exe` reverses the disconnect workflow and performs progressively deeper recovery:

1. Enable the enclosure's ASMedia PCIe switch.
2. Scan for Plug and Play changes.
3. Re-enable NVIDIA PCI functions directly beneath the dock bridge, including Code 22 devices.
4. If the GPU reports Code 43, restart the GPU and check it again.
5. If necessary, perform one GPU disable/enable cycle.
6. Restart the exact downstream PCIe bridge.
7. As a final software recovery, restart the enclosure's ASMedia USB4 router and rebuild the PCIe tunnel.

A physical cable reconnect or dock power cycle can still be necessary because software cannot reproduce an electrical power cut.

## Compatibility

The utilities identify hardware by topology rather than GPU model name:

- NVIDIA PCI vendor ID: `VEN_10DE`
- ASMedia PCIe switch: `VEN_1B21&DEV_2461`
- ASMedia USB4 router: `USB4\VID_174C&PID_2461`
- One NVIDIA display adapter installed in the enclosure

This supports GeForce RTX/GTX, Titan, Quadro, and professional RTX cards that use the same dock topology. Internal GPUs and NVIDIA devices on unrelated PCIe paths are ignored.

Other USB4 or Thunderbolt enclosures may use different bridge/router IDs and are not currently supported without a source change.

## Safety and limitations

- Always save work before disconnecting the eGPU.
- A physical unplug cannot be made safe retroactively. Use **Disconnect eGPU** before removing the cable.
- The tools require administrator privileges to change Plug and Play device state.
- The disconnect workflow uses a non-persistent Configuration Manager disable; reconnect explicitly restores docked NVIDIA functions.
- Code 43 can indicate a stuck USB4/PCIe tunnel, driver failure, inadequate power, poor card seating, or hardware failure. Software recovery is not guaranteed.
- The project is provided without warranty. Review the source and hardware IDs before adapting it to another system.

## Troubleshooting

| Symptom | Meaning and next step |
| --- | --- |
| Code 22 | The device is disabled. Run Reconnect or choose **Reconnect now**. |
| Code 43 | Windows could not start the GPU. Reconnect runs GPU, bridge, and USB4-router recovery automatically. |
| GPU does not reappear | Physically reconnect the USB4 cable. If needed, shut down and remove dock power for 30 seconds. |
| External display stays blank | Confirm the GPU is healthy in Device Manager, then reselect the display input or Windows display mode. |
| SmartScreen warning | The release binaries are currently unsigned. Verify the SHA-256 file attached to the release. |

## Build from source

Requirements:

- Windows 11
- Windows PowerShell
- .NET Framework 4.x C# compiler included with Windows

From the repository root:

```powershell
.\build.ps1
```

Outputs:

```text
dist\eGPU-Eject.exe
dist\eGPU-Reconnect.exe
dist\eGPU-Tray.exe
```

The applications use Windows Configuration Manager, SetupAPI, device-change notifications, PnPUtil, and Task Scheduler. They have no third-party runtime dependencies.

## Project structure

```text
assets/                Application icons and source PNGs
src/EgpuEject.cs       Disconnect workflow and standalone UI
src/EgpuReconnect.cs   Reconnect and Code 22/43 recovery
src/EgpuTray.cs        Tray monitor and combined application
build.ps1              Builds all three executables
```

## License

[MIT](LICENSE)
