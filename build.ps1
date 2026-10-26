# Builds the self-contained binaries and the installer: dist\EnshroudedManager-Setup-<version>.exe
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Remove-Item publish, dist -Recurse -Force -ErrorAction SilentlyContinue
$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=true",
            "-p:EnableCompressionInSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=none", "-p:Version=$Version", "-nologo")
dotnet publish src/EnshroudedManager.Service @common -o publish/Service
if ($LASTEXITCODE) { throw "Service build failed" }
dotnet publish src/EnshroudedManager.Desktop @common -o publish/Desktop
if ($LASTEXITCODE) { throw "Desktop build failed" }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc "/DAppVersion=$Version" installer\EnshroudedManager.iss
if ($LASTEXITCODE) { throw "Installer build failed" }
Get-ChildItem dist
