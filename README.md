# eGPU Eject

A controller-friendly Windows utility that safely ejects an external NVIDIA GeForce RTX 4060 Ti. Launch it from Windows or Xbox full-screen mode and it immediately asks Windows Plug and Play to eject the GPU. If Windows reports that the GPU is busy, the app stays open with a large **Retry** button.

## Safety behavior

The normal path calls the documented Windows Configuration Manager eject request and does not force-disable the GPU. Windows can refuse the request while a game, display, driver, or other process is using it. Unplug only after the app says it is safe.

If normal eject is vetoed, **Force disable eGPU (Admin)** uses the Windows Configuration Manager API to disable the RTX 4060 Ti first and then makes a best-effort attempt to disable HDMI audio. Display-first ordering lets Windows migrate the desktop before releasing audio endpoints; an audio veto no longer prevents GPU shutdown. The disable is non-persistent, so the enclosure can enumerate normally when reconnected. This avoids bridge disable and forced devnode removal operations, both of which some USB4 systems defer until reboot. Administrator approval is required, and displays connected to the eGPU can blank immediately. Use it only after saving work and closing GPU applications.

This first release intentionally targets the RTX 4060 Ti by device name so it will not accidentally eject another display adapter.

## Use

1. Download `eGPU-Eject.exe` from Releases.
2. Close games and GPU-intensive programs.
3. Run the app. No installation or administrator prompt is required.
4. Wait for **Safe to unplug** before disconnecting the cable.

For controller use, add the EXE as an app in the Windows/Xbox full-screen experience. Launching it begins the eject request automatically. If Retry appears, activate it with the controller's confirm/A action. Escape or the controller back action closes the app.

## Build

Windows includes the .NET Framework compiler used by this project:

```powershell
.\build.ps1
```

The output is `dist\eGPU-Eject.exe` and has no external runtime dependencies beyond Windows .NET Framework 4.x.

## License

MIT
