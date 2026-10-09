<#
.SYNOPSIS
    Tests launcher prerequisites without installing packages or launching applications.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Test-Launcher {
    param([string]$Path, [bool]$HasWar2, [string]$FrameworkVersion, [bool]$ExpectBuild,
        [string]$NuGetConfig, [string]$WarArchitecture = 'ARM64')

    $queries = [Collections.Generic.List[string]]::new()
    function Get-AppxPackage {
        param([string]$Name)
        $queries.Add($Name)
        if ($Name -like '*AionInstruct*') {
            return [PSCustomObject]@{
                Name = 'Microsoft.AionInstructPreview.Framework.1.0'
                Version = $FrameworkVersion
                Architecture = 'ARM64'
            }
        }
        if ($Name -eq 'Microsoft.WindowsAppRuntime.2*' -and $HasWar2) {
            return [PSCustomObject]@{
                Name = 'Microsoft.WindowsAppRuntime.2'
                Version = '2.0.1.0'
                Architecture = $WarArchitecture
            }
        }
    }
    function Get-ChildItem { param($Path, $Filter) return 'SDK-package-present' }
    function dotnet {
        if ($NuGetConfig) {
            $expected = '-p:RestoreConfigFile=' + (Get-Item -LiteralPath $NuGetConfig).FullName
            if ($expected -notin $args) { throw 'CONFIG_OVERRIDE_NOT_FORWARDED' }
        } elseif (@($args | Where-Object { $_ -like '-p:RestoreConfigFile=*' }).Count) {
            throw 'UNEXPECTED_CONFIG_OVERRIDE'
        }
        throw 'BUILD_REACHED'
    }

    $message = ''
    try {
        if ($NuGetConfig) { & $Path -NuGetConfig $NuGetConfig }
        else { & $Path }
    }
    catch { $message = $_.Exception.Message }
    if (($message -eq 'BUILD_REACHED') -ne $ExpectBuild) {
        throw "Unexpected prerequisite result for $Path : $message"
    }
    if (-not $ExpectBuild -and $message -notmatch 'Install|not installed') {
        throw "Missing prerequisite did not provide installation guidance: $message"
    }
    if ($queries -contains 'Microsoft.WindowsAppRuntime.1.8*') {
        throw 'Launcher still queries WAR 1.8.'
    }
}

foreach ($relative in @('unpackaged-console\Run.ps1', 'unpackaged-wpf\Run.ps1')) {
    $path = Join-Path $root $relative
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.3' -ExpectBuild $true
    Test-Launcher -Path $path -HasWar2 $false -FrameworkVersion '1.0.0.3' -ExpectBuild $false
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.2' -ExpectBuild $false
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.1' -ExpectBuild $false
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.0' -ExpectBuild $false
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.3' -ExpectBuild $false `
        -WarArchitecture 'x64'
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.3' -ExpectBuild $true `
        -NuGetConfig (Join-Path $root 'nuget.config')
}
foreach ($relative in @('Bootstrap.ps1', 'unpackaged-console\Run.ps1', 'unpackaged-wpf\Run.ps1')) {
    $rejected = $false
    try { & (Join-Path $root $relative) -NuGetConfig (Join-Path $root ([guid]::NewGuid().ToString() + '.config')) }
    catch { $rejected = $_.CategoryInfo.Category -eq 'ObjectNotFound' }
    if (-not $rejected) { throw "$relative did not reject the missing config before setup." }
}
foreach ($parameter in @('FrameworkMsixPath', 'SdkNuGetPath')) {
    $rejected = $false
    $singleArgument = @{ $parameter = (Join-Path $root 'nuget.config') }
    try {
        & (Join-Path $root 'Bootstrap.ps1') @singleArgument
    } catch {
        $rejected = $_.Exception.Message -match 'Specify -FrameworkMsixPath and -SdkNuGetPath together'
    }
    if (-not $rejected) { throw "Bootstrap did not reject lone -$parameter before setup." }
}
Write-Output 'PASS: 14 launcher scenarios and 5 missing-input checks; no packages or apps changed.'
