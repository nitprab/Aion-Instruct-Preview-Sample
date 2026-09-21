<#
.SYNOPSIS
    Tests launcher prerequisites without installing packages or launching applications.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Test-Launcher {
    param([string]$Path, [bool]$HasWar2, [string]$FrameworkVersion, [bool]$ExpectBuild)

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
            return [PSCustomObject]@{ Name = 'Microsoft.WindowsAppRuntime.2' }
        }
    }
    function Get-ChildItem { param($Path, $Filter) return 'SDK-package-present' }
    function dotnet { throw 'BUILD_REACHED' }

    $message = ''
    try { & $Path }
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
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.1' -ExpectBuild $true
    Test-Launcher -Path $path -HasWar2 $false -FrameworkVersion '1.0.0.1' -ExpectBuild $false
    Test-Launcher -Path $path -HasWar2 $true -FrameworkVersion '1.0.0.0' -ExpectBuild $false
}
Write-Output 'PASS: 6 launcher prerequisite scenarios; no packages or apps changed.'
