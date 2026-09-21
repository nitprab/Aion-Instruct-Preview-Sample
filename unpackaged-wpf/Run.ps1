# Run.ps1 -- build and run the unpackaged WPF chat sample.
#
# Prereqs (same model dependency as the packaged AionInstructPreview.Chat):
#   - The Aion Instruct Preview SDK framework MSIX is installed
#     (Microsoft.AionInstructPreview.Framework.1.0). See the repo root README
#     for how to install it.
#   - Windows App Runtime 2 is installed
#     (winget install --id Microsoft.WindowsAppRuntime.2.0).
#   - .NET SDK 9 or later.
#
# Unlike the packaged sample, nothing is copied locally: the winmd comes from
# the SDK NuGet at build time, and the model files + native DLLs are consumed
# in place from the installed framework package at runtime.

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

Write-Host "Checking for the installed Aion Instruct Preview framework package..."
$pkg = Get-AppxPackage "Microsoft.AionInstructPreview.Framework*" |
    Sort-Object Version -Descending |
    Select-Object -First 1

if ($null -eq $pkg -or [version]$pkg.Version -lt [version]'1.0.0.1') {
    Write-Error "Install or upgrade Aion Instruct Preview framework to 1.0.0.1 or newer (see the repo root README), then re-run."
    exit 1
}

Write-Host ("Using framework {0} {1} ({2})." -f $pkg.Name, $pkg.Version, $pkg.Architecture)

# Inference uses bundled WinML/ORT; WAR 2 supplies the remaining Windows App SDK types.
if (-not (Get-AppxPackage 'Microsoft.WindowsAppRuntime.2*')) {
    Write-Error "Windows App Runtime 2 is not installed. Run: winget install --id Microsoft.WindowsAppRuntime.2.0"
    exit 1
}
$nugetLocal = Join-Path (Split-Path $here -Parent) "nuget-local"
$sdkNupkg = Get-ChildItem -Path $nugetLocal -Filter "AionInstructPreview.Text.Framework*.nupkg" -ErrorAction SilentlyContinue
if (-not $sdkNupkg) {
    Write-Error "SDK NuGet package not found in $nugetLocal. Run .\Bootstrap.ps1 from the repo root first (it downloads the SDK), or follow the 'Getting the SDK NuGet' section of the README."
    exit 1
}

dotnet run --project (Join-Path $here "AionInstructPreview.Chat.Wpf.csproj") -c Release
