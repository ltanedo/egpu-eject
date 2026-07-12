$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw 'The .NET Framework C# compiler was not found.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:"$root\assets\egpu-eject.ico" /out:"$dist\eGPU-Eject.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "$root\src\EgpuEject.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
Write-Host "Built $dist\eGPU-Eject.exe"

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:"$root\assets\egpu-eject.ico" /out:"$dist\eGPU-Reconnect.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "$root\src\EgpuReconnect.cs"
if ($LASTEXITCODE -ne 0) { throw "Reconnect build failed with exit code $LASTEXITCODE" }
Write-Host "Built $dist\eGPU-Reconnect.exe"
