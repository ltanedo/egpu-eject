# eGPU Eject

A controller-friendly Windows utility that disconnects an NVIDIA graphics card in the supported ASMedia USB4 eGPU dock. Launching it from Windows or Xbox full-screen mode immediately requests administrator approval and runs the force-disable sequence that takes the eGPU offline.

The release also includes `eGPU-Reconnect.exe`, a companion utility for this PC with a distinct blue reconnect-arrow icon. It requests administrator approval, enables the dock's ASMedia PCIe switch ports by hardware ID, and asks Windows to scan for the installed NVIDIA card and connected displays. This survives GPU swaps and device-instance changes.

Reconnect also checks the docked NVIDIA adapter's Windows device problem code. For Code 43 it restarts that exact GPU, checks again, and if necessary performs one disable/enable cycle plus another hardware scan. If Code 43 remains, it restarts the exact ASMedia downstream PCIe bridge. As a final recovery stage it restarts only this enclosure's ASMedia USB4 router (`USB4\VID_174C&PID_2461`), waits for the PCIe tunnel to rebuild, rescans, and checks the GPU again. This is the closest safe software equivalent to unplugging and reconnecting the laptop-side USB4 cable, though it cannot reproduce a true electrical power cut. It never targets an internal or unrelated NVIDIA adapter and does not persistently disable the bridge or router.

Before checking Code 43, Reconnect enumerates the NVIDIA PCI functions directly beneath the dock bridge and enables each exact devnode. This reverses the eject utility's normal Code 22 state for the GPU, HDMI audio, and other NVIDIA companion functions without enabling unrelated or internal NVIDIA devices.

## Safety behavior

The normal path calls the documented Windows Configuration Manager eject request and does not force-disable the GPU. Windows can refuse the request while a game, display, driver, or other process is using it. Unplug only after the app says it is safe.

If normal eject is vetoed, **Force disable eGPU (Admin)** finds a present display adapter with NVIDIA's PCI vendor ID (`VEN_10DE`) whose direct parent is the supported ASMedia dock bridge. It disables that GPU first and then makes a best-effort attempt to disable its NVIDIA sibling functions such as HDMI audio. Display-first ordering lets Windows migrate the desktop before releasing audio endpoints; an audio veto no longer prevents GPU shutdown. The disable is non-persistent, so the enclosure can enumerate normally when reconnected. Administrator approval is required, and displays connected to the eGPU can blank immediately. Use it only after saving work and closing GPU applications.

Assuming only one NVIDIA card is installed in the dock, the eject utility supports GeForce RTX/GTX, Titan, RTX professional, Quadro, and other NVIDIA display cards while ignoring internal GPUs and NVIDIA devices on unrelated PCIe paths.

## Use

1. Download `eGPU-Eject.exe` from Releases.
2. Save work and close games or GPU-intensive programs.
3. Run the app and approve the Windows administrator prompt.
4. Wait for **Safe to unplug** before disconnecting the cable.

For controller use, add the EXE as an app in the Windows/Xbox full-screen experience. Launching it immediately requests elevation and begins the working force-disable sequence; no second in-app confirmation is required.

## Build

Windows includes the .NET Framework compiler used by this project:

```powershell
.\build.ps1
```

The outputs are `dist\eGPU-Eject.exe` and `dist\eGPU-Reconnect.exe`. Neither has external runtime dependencies beyond Windows .NET Framework 4.x.

## License

MIT
