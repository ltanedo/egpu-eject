# eGPU Eject

A controller-friendly Windows utility that disconnects an external NVIDIA GeForce RTX 4060 Ti. Launching it from Windows or Xbox full-screen mode immediately requests administrator approval and runs the force-disable sequence that takes the GPU offline.

The release also includes `eGPU-Reconnect.exe`, a companion utility for this PC. It requests administrator approval, re-enables the ASMedia downstream PCIe bridge above the eGPU, and asks Windows to scan for the RTX 4060 Ti and connected displays.

## Safety behavior

The normal path calls the documented Windows Configuration Manager eject request and does not force-disable the GPU. Windows can refuse the request while a game, display, driver, or other process is using it. Unplug only after the app says it is safe.

If normal eject is vetoed, **Force disable eGPU (Admin)** uses the Windows Configuration Manager API to disable the RTX 4060 Ti first and then makes a best-effort attempt to disable HDMI audio. Display-first ordering lets Windows migrate the desktop before releasing audio endpoints; an audio veto no longer prevents GPU shutdown. The disable is non-persistent, so the enclosure can enumerate normally when reconnected. This avoids bridge disable and forced devnode removal operations, both of which some USB4 systems defer until reboot. Administrator approval is required, and displays connected to the eGPU can blank immediately. Use it only after saving work and closing GPU applications.

This first release intentionally targets the RTX 4060 Ti by device name so it will not accidentally eject another display adapter.

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
