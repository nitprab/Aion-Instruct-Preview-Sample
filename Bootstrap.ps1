# Bootstrap.ps1 -- one-shot setup for AionInstructPreview.Chat.
#
# Picks up where 'git clone' leaves off:
#   1. Verifies prereqs (PS arch, Developer Mode) and auto-installs the
#      WAR 2 + WAR 1.8 runtimes via winget when they're missing.
#   2. Downloads the latest signed Aion Instruct Preview release from GitHub if the
#      framework MSIX is not already installed; installs it.
#   3. Drops the SDK NuGet into ./nuget-local/.
#   4. Builds and launches AionInstructPreview.Chat via 'dotnet run' (which
#      registers the loose build-output layout as a development package).
#
# Re-running is idempotent: the framework at the matching version is detected
# and skipped, and already-present runtimes are left as-is. Older framework
# installs are NOT auto-removed -- the script prints exact recovery commands
# and exits so you can decide whether to clean up.
#
# Usage:
#     .\Bootstrap.ps1
#     .\Bootstrap.ps1 -SkipLaunch    # install prereqs only, do not build/launch
#     .\Bootstrap.ps1 -Verbose       # show every step
#
# Stuck after Bootstrap finishes? Run scripts\Diagnose-AionInstructPreview.ps1 -- it
# captures the SDK's own EP decision log line via OutputDebugString.

#Requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = `
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$RepoOwner       = 'microsoft'
$RepoName        = 'Aion-Instruct-Preview-Sample'
$FrameworkPkgId  = 'Microsoft.AionInstructPreview.Framework.1.0'
$ConsumerPkgId   = 'AionInstructPreviewChat'
$ConsumerVersion = '1.0.0.0'
$WarPkgId        = 'Microsoft.WindowsAppRuntime.2'
$War18PkgId      = 'Microsoft.WindowsAppRuntime.1.8'
$War18MinVersion = '8000.836.2153.0'

function Write-Step  { param([string]$Msg) Write-Host "[bootstrap] $Msg" -ForegroundColor Cyan }
function Write-OK    { param([string]$Msg) Write-Host "[bootstrap] $Msg" -ForegroundColor Green }
function Write-Skip  { param([string]$Msg) Write-Host "[bootstrap] $Msg" -ForegroundColor DarkGray }
function Write-Fail  { param([string]$Msg) Write-Host "[bootstrap] $Msg" -ForegroundColor Red }

function Stop-WithRecovery {
    param([string]$Title, [string[]]$Recovery)
    Write-Host ''
    Write-Fail "FAIL: $Title"
    Write-Host ''
    Write-Host 'Recovery:' -ForegroundColor Yellow
    foreach ($line in $Recovery) { Write-Host "    $line" -ForegroundColor Yellow }
    Write-Host ''
    Write-Host 'After fixing, re-run .\Bootstrap.ps1' -ForegroundColor Yellow
    exit 1
}

# Ensure a Windows App Runtime is present for $Arch (optionally at/above
# $MinVersion). If missing, install it with winget and re-check. winget's exit
# code is deliberately ignored -- "already installed" and several success-ish
# states return non-zero, so the AppX re-check is the source of truth. Falls
# back to manual recovery instructions if winget is unavailable or the runtime
# still isn't present afterward.
function Ensure-Runtime {
    param(
        [string]$Label,
        [string]$PkgId,
        [string]$WingetId,
        [string]$Arch,
        [string]$MinVersion
    )

    $find = {
        Get-AppxPackage -Name $PkgId -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Architecture -eq $Arch -and
                (-not $MinVersion -or [version]$_.Version -ge [version]$MinVersion)
            }
    }

    $pkg = & $find
    if ($pkg) {
        Write-OK "$Label ($Arch) v$($pkg[0].Version) installed"
        return
    }

    $manualRecovery = @(
        '# winget is the fast path:',
        "    winget install --id $WingetId",
        '# or grab the installer:',
        '    https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads'
    )

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Stop-WithRecovery `
            -Title "$Label ($Arch) is not installed, and winget (App Installer) is unavailable to install it automatically" `
            -Recovery $manualRecovery
    }

    Write-Step "$Label ($Arch) not found -- installing with winget (may prompt for elevation) ..."
    # Exit code intentionally not checked; the re-check below is authoritative.
    winget install --id $WingetId --exact --accept-package-agreements --accept-source-agreements | Out-Host

    $pkg = & $find
    if (-not $pkg) {
        Stop-WithRecovery `
            -Title "$Label ($Arch) still not present after the winget install attempt" `
            -Recovery $manualRecovery
    }
    Write-OK "$Label ($Arch) v$($pkg[0].Version) installed"
}

# --- 0. Banner --------------------------------------------------------------
Write-Host ''
Write-Host '=== AionInstructPreview.Chat Bootstrap ===' -ForegroundColor Cyan
Write-Host ''

# --- 1. Arch detection ------------------------------------------------------
# Detect the native machine architecture so the installed framework and app
# match the hardware, even when PowerShell is running under emulation.
$emulatedArch = $env:PROCESSOR_ARCHITECTURE
$nativeArch   = $env:PROCESSOR_ARCHITEW6432
if ($nativeArch) {
    $rawArch = $nativeArch
    Write-Step "Running under emulation/WOW64 (process arch $emulatedArch); using native arch $nativeArch"
} else {
    $rawArch = $emulatedArch
}

$arch = switch ($rawArch) {
    'ARM64' { 'ARM64' }
    default { $null }
}
if ($rawArch -eq 'AMD64') {
    Stop-WithRecovery `
        -Title 'x64 (Intel/AMD) support is coming soon.' `
        -Recovery @(
            'This preview of Aion Instruct Preview supports ARM64 Copilot+ PCs (Snapdragon, QNN NPU) only.',
            'x64 (Intel/AMD) support is coming soon — check the releases page for updates.',
            'Run Bootstrap.ps1 on an ARM64 Snapdragon Copilot+ PC to try the preview today.'
        )
}
if (-not $arch) {
    Stop-WithRecovery `
        -Title "Unsupported processor architecture=$rawArch (process arch $emulatedArch, native arch '$nativeArch')" `
        -Recovery @(
            'AionInstructPreview.Chat ships an ARM64 build only in this preview.',
            'Launch a 64-bit PowerShell on an ARM64 Snapdragon Copilot+ PC and re-run.'
        )
}
Write-Step "Architecture: $arch"

# --- 2. WAR 2 runtime check -------------------------------------------------
Ensure-Runtime -Label 'WAR 2' -PkgId $WarPkgId -WingetId 'Microsoft.WindowsAppRuntime.2.0' -Arch $arch

# --- 2a. WAR 1.8 runtime check ----------------------------------------------
# The on-device model runs on the WinML stack from Windows App Runtime 1.8.
Ensure-Runtime -Label 'WAR 1.8' -PkgId $War18PkgId -WingetId 'Microsoft.WindowsAppRuntime.1.8' -Arch $arch -MinVersion $War18MinVersion

# --- 2b. Developer Mode check -----------------------------------------------
# dotnet run registers the loose build-output layout as a development package,
# which requires Developer Mode.
$devModeKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
$devMode = $null
try {
    $devMode = (Get-ItemProperty -Path $devModeKey -Name 'AllowDevelopmentWithoutDevLicense' -ErrorAction Stop).AllowDevelopmentWithoutDevLicense
} catch {}
if ($devMode -ne 1) {
    Write-Step 'Developer Mode is not enabled -- enabling now (UAC prompt) ...'
    $regCmd = 'reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v AllowDevelopmentWithoutDevLicense /d 1'
    Start-Process -FilePath powershell -ArgumentList '-NoProfile', '-Command', $regCmd -Verb RunAs -Wait
    $devMode = $null
    try {
        $devMode = (Get-ItemProperty -Path $devModeKey -Name 'AllowDevelopmentWithoutDevLicense' -ErrorAction Stop).AllowDevelopmentWithoutDevLicense
    } catch {}
    if ($devMode -ne 1) {
        Stop-WithRecovery `
            -Title 'Developer Mode could not be enabled' `
            -Recovery @(
                'Enable it manually:',
                '    Settings -> Privacy & security -> For developers -> Developer Mode -> On',
                'Or via an elevated PowerShell:',
                "    $regCmd"
            )
    }
}
Write-OK 'Developer Mode enabled'

# --- 2c. NuGet cache/package path env var validation ------------------------
# NuGet honors NUGET_HTTP_CACHE_PATH and NUGET_PACKAGES for restore. If a user
# points either at a path that doesn't exist, restore fails deep in the build
# with a confusing error -- validate up front and fail fast with recovery steps.
# Each variable is checked independently; an unset variable is not an error.
function Test-NuGetPathEnvVar {
    param([string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return
    }

    if (-not (Test-Path -LiteralPath $value -PathType Container)) {
        Stop-WithRecovery `
            -Title "$Name is set to '$value', which is not accessible" `
            -Recovery @(
                'Please point the variable at an existing directory, create it, or clear it:',
                "    New-Item -ItemType Directory -Force -Path '$value'",
                '# or clear it for this session:',
                "    Remove-Item Env:\$Name",
                '# or fix it permanently under:',
                '    Settings -> System -> About -> Advanced system settings -> Environment Variables'
            )
    }

    Write-OK "$Name -> $value"
}

Test-NuGetPathEnvVar -Name 'NUGET_HTTP_CACHE_PATH'
Test-NuGetPathEnvVar -Name 'NUGET_PACKAGES'

# --- 3. Discover latest release via the public GitHub Releases API ----------
# This repo is public, so the latest signed release is fetched straight from
# the unauthenticated GitHub REST API over HTTPS -- no GitHub CLI, no token,
# no sign-in. api.github.com requires a User-Agent header on every request.
$ProgressPreference = 'SilentlyContinue'  # speeds up Invoke-WebRequest ~100x on PS 5.1
$ghHeaders = @{
    'User-Agent' = 'AionInstructPreview-Bootstrap'
    'Accept'     = 'application/vnd.github+json'
}
$latestUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/latest"

Write-Step "Discovering latest release from $latestUrl ..."
try {
    $release = Invoke-RestMethod -Uri $latestUrl -Headers $ghHeaders -ErrorAction Stop
} catch {
    Stop-WithRecovery `
        -Title "Could not query the latest release ($($_.Exception.Message))" `
        -Recovery @(
            "Visit https://github.com/$RepoOwner/$RepoName/releases in a browser to confirm a release exists.",
            'Check your internet connection / proxy, then re-run.'
        )
}
if (-not $release -or -not $release.tag_name) {
    Stop-WithRecovery `
        -Title 'No published release found' `
        -Recovery @(
            "Visit https://github.com/$RepoOwner/$RepoName/releases in a browser to confirm a release exists.",
            'If the page is empty, the SDK pipeline has not published a release yet.'
        )
}
$tag = $release.tag_name
$targetFwVersion = $tag -replace '^v', ''
$assets = @($release.assets)
Write-OK "Latest release: $tag ($($release.name))"

# Resolve a release asset's download URL by exact file name. Returns the
# browser_download_url (a plain HTTPS link served without auth on a public repo).
function Get-AssetUrl {
    param([string]$Name)
    $asset = $assets | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if (-not $asset) {
        Stop-WithRecovery `
            -Title "Release $tag has no asset named '$Name'" `
            -Recovery @(
                "Visit https://github.com/$RepoOwner/$RepoName/releases/tag/$tag and check the asset list.",
                "Expected asset name: $Name"
            )
    }
    return $asset.browser_download_url
}

# Asset names follow a fixed pattern: AionInstructPreview.LanguageModel.Framework_<ver>_<arch>.msix
# and AionInstructPreview.Text.Framework.<nupkgVer>.nupkg.
# The MSIX uses the 4-part package version (e.g. 1.0.0.0); the NuGet asset uses the
# 3-part SemVer (e.g. 1.0.0). Derive the 3-part version for the nupkg name.
$expectedMsixName = "AionInstructPreview.LanguageModel.Framework_${targetFwVersion}_${arch}.msix"
$nupkgVersion = ($targetFwVersion -split '\.')[0..2] -join '.'
$expectedNupkgName = "AionInstructPreview.Text.Framework.${nupkgVersion}.nupkg"

# --- 4. Framework MSIX state -----------------------------------------------
$fw = Get-AppxPackage -Name $FrameworkPkgId -ErrorAction SilentlyContinue |
      Where-Object { $_.Architecture -eq $arch }
if ($fw -and $fw.Version -eq $targetFwVersion) {
    Write-Skip "Aion Instruct Preview framework MSIX already installed at v$($fw.Version) -- skipping download/install"
} elseif ($fw) {
    Stop-WithRecovery `
        -Title "Aion Instruct Preview framework already installed at v$($fw.Version); release ships v$targetFwVersion" `
        -Recovery @(
            '# Uninstall any dependent test packages first if needed, then:',
            "    Get-AppxPackage -Name $FrameworkPkgId | Remove-AppxPackage",
            '# If that errors with HRESULT 0x80073CF3, a sibling package depends on the framework.',
            "    Get-AppxPackage | Where-Object { `$_.Dependencies.Name -contains '$FrameworkPkgId' } |",
            '        ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }'
        )
} else {
    $stage = Join-Path $env:TEMP "Aion Instruct Preview-bootstrap-$tag"
    if (-not (Test-Path $stage)) { New-Item -ItemType Directory -Path $stage | Out-Null }
    Write-Step "Downloading $expectedMsixName (~1.3 GB; this is the slow step, please wait) ..."
    $msixPath = Join-Path $stage $expectedMsixName
    $msixUrl = Get-AssetUrl -Name $expectedMsixName
    try {
        Invoke-WebRequest -Uri $msixUrl -Headers $ghHeaders -OutFile $msixPath -ErrorAction Stop
    } catch {
        Stop-WithRecovery `
            -Title "Download failed for $expectedMsixName ($($_.Exception.Message))" `
            -Recovery @(
                "Visit https://github.com/$RepoOwner/$RepoName/releases/tag/$tag and check the asset list.",
                "Expected asset name: $expectedMsixName"
            )
    }
    Write-Step "Installing framework MSIX ..."
    Add-AppxPackage -Path $msixPath
    $fw = Get-AppxPackage -Name $FrameworkPkgId -ErrorAction SilentlyContinue |
          Where-Object { $_.Architecture -eq $arch }
    if (-not $fw) {
        Stop-WithRecovery `
            -Title 'Framework MSIX install reported success but the package is not registered.' `
            -Recovery @(
                'Inspect with:',
                "    Add-AppxPackage -Path '$msixPath'",
                'Watch its error output. Common causes: cert chain, mismatched arch, side-by-side conflict.'
            )
    }
    Write-OK "Installed framework v$($fw.Version)"
}

# --- 5. SDK nupkg in nuget-local/ ------------------------------------------
$nugetLocal = Join-Path $PSScriptRoot 'nuget-local'
$expectedNupkg = Join-Path $nugetLocal $expectedNupkgName
if (Test-Path $expectedNupkg) {
    Write-Skip "$expectedNupkgName already in nuget-local/ -- skipping download"
} else {
    # Wipe stale older-version nupkgs so NuGet restore picks the new one cleanly.
    Get-ChildItem $nugetLocal -Filter 'AionInstructPreview.Text.Framework*.nupkg' -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Skip "Removing stale $($_.Name) from nuget-local/"
            Remove-Item $_.FullName -Force
        }
    Write-Step "Downloading $expectedNupkgName ..."
    $nupkgUrl = Get-AssetUrl -Name $expectedNupkgName
    try {
        Invoke-WebRequest -Uri $nupkgUrl -Headers $ghHeaders -OutFile (Join-Path $nugetLocal $expectedNupkgName) -ErrorAction Stop
    } catch {
        Stop-WithRecovery `
            -Title "Download failed for $expectedNupkgName ($($_.Exception.Message))" `
            -Recovery @(
                "Visit https://github.com/$RepoOwner/$RepoName/releases/tag/$tag and check the asset list.",
                "Expected asset name: $expectedNupkgName"
            )
    }
    Write-OK "Dropped $expectedNupkgName in nuget-local/"
}

# --- 6. Build, register (loose layout) and launch via dotnet run ------------
# dotnet run builds, registers the loose-layout development package, and launches the app.
$csproj = Join-Path $PSScriptRoot 'AionInstructPreview.Chat.csproj'

if ($SkipLaunch) {
    Write-Host ''
    Write-OK 'Bootstrap complete (skipped build/launch).'
    Write-Host 'Build and run manually:' -ForegroundColor Cyan
    Write-Host "    dotnet run --project `"$csproj`" --launch-profile `"AionInstructPreview.Chat`" -c Release -p:Platform=$arch" -ForegroundColor Cyan
    return
}

# Remove any installed consumer MSIX before registering the loose-layout app.
$existing = Get-AppxPackage -Name $ConsumerPkgId -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Removing previously-installed $ConsumerPkgId (dotnet run uses a development registration) ..."
    $existing | Remove-AppxPackage
}

Write-Step "Building and launching AionInstructPreview.Chat ($arch, Release) via dotnet run ..."
& dotnet run --project "$csproj" --launch-profile "AionInstructPreview.Chat" -c Release -p:Platform=$arch | Out-Host
if ($LASTEXITCODE -ne 0) {
    Stop-WithRecovery `
        -Title 'dotnet run failed' `
        -Recovery @(
            'Re-run directly for full output:',
            "    dotnet run --project `"$csproj`" --launch-profile `"AionInstructPreview.Chat`" -c Release -p:Platform=$arch",
            'Most likely causes:',
            '  - Developer Mode is off (Settings -> Privacy & security -> For developers).',
            '  - The .NET 9 SDK is not installed (run: dotnet --info to confirm).',
            '  - Building under C:\Windows\System32 -- UAC file virtualization redirects the',
            '    obj\ codegen writes. Clone and build under your user profile (e.g. C:\repos)',
            '    from a normal (non-elevated) PowerShell; building does not require admin.'
        )
}

Write-Host ''
Write-OK 'Bootstrap complete. Aion Instruct Preview Chat is loading.'
Write-Host 'First launch on a cold cache takes ~3-5 min for the NPU model compile.' -ForegroundColor DarkGray
Write-Host 'Stuck longer than that? Run: .\scripts\Diagnose-AionInstructPreview.ps1' -ForegroundColor DarkGray
