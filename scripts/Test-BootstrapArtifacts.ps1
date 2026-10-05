<#
.SYNOPSIS
    Tests bootstrap artifact identity and effective NuGet cache selection without deployment.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $root 'Bootstrap.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Bootstrap.ps1 contains syntax errors.' }
foreach ($name in @('Assert-SdkNuGetCacheMatches', 'Assert-LocalFrameworkMatches')) {
    $definition = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if (-not $definition) { throw "Missing bootstrap function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)
    try { & $Action } catch {
        if ($_.Exception.Message -like "*$Message*") { return }
        throw
    }
    throw "Expected rejection: $Message"
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('AionBootstrapTests-' + [guid]::NewGuid())
$previousCache = $env:NUGET_PACKAGES
try {
    New-Item -ItemType Directory -Path $temporary | Out-Null
    $env:NUGET_PACKAGES = $null
    $config = Join-Path $temporary 'nuget.config'
    [IO.File]::WriteAllText($config,
        '<configuration><config><add key="globalPackagesFolder" value="configured-cache" /></config></configuration>')
    $package = Join-Path $temporary 'input.nupkg'
    [IO.File]::WriteAllText($package, 'new SDK')
    $cacheRelative = 'aioninstructpreview.text.framework\1.0.1\aioninstructpreview.text.framework.1.0.1.nupkg'
    $cached = Join-Path (Join-Path $temporary 'configured-cache') $cacheRelative
    New-Item -ItemType Directory -Path (Split-Path $cached -Parent) -Force | Out-Null
    $check = @{
        PackagePath = $package
        Version = '1.0.1'
        ConfigFile = $config
        ProjectDirectory = $root
    }
    Assert-SdkNuGetCacheMatches @check
    [IO.File]::WriteAllText($cached, 'stale SDK')
    Assert-Rejected { Assert-SdkNuGetCacheMatches @check } 'contains a different Aion SDK'
    Copy-Item -LiteralPath $package -Destination $cached -Force
    Assert-SdkNuGetCacheMatches @check

    $env:NUGET_PACKAGES = Join-Path $temporary 'environment-cache'
    [IO.File]::WriteAllText($cached, 'stale config SDK ignored by environment override')
    Assert-SdkNuGetCacheMatches @check
    $environmentPackage = Join-Path $env:NUGET_PACKAGES $cacheRelative
    New-Item -ItemType Directory -Path (Split-Path $environmentPackage -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($environmentPackage, 'stale environment SDK')
    Assert-Rejected { Assert-SdkNuGetCacheMatches @check } 'contains a different Aion SDK'
    Copy-Item -LiteralPath $package -Destination $environmentPackage -Force
    Assert-SdkNuGetCacheMatches @check

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $layout = Join-Path $temporary 'layout'
    $installed = Join-Path $temporary 'installed'
    New-Item -ItemType Directory -Path $layout, $installed | Out-Null
    $blockMap = Join-Path $layout 'AppxBlockMap.xml'
    [IO.File]::WriteAllText($blockMap, '<BlockMap>build A</BlockMap>')
    $msix = Join-Path $temporary 'framework.msix'
    [IO.Compression.ZipFile]::CreateFromDirectory($layout, $msix)
    Assert-Rejected { Assert-LocalFrameworkMatches $msix $installed } 'block map is missing'
    Copy-Item $blockMap $installed
    Assert-LocalFrameworkMatches -MsixPath $msix -InstallLocation $installed
    [IO.File]::WriteAllText((Join-Path $installed 'AppxBlockMap.xml'), '<BlockMap>build B</BlockMap>')
    Assert-Rejected { Assert-LocalFrameworkMatches $msix $installed } 'same version but different contents'
    Write-Output 'PASS: 6 effective-cache scenarios and 3 framework-content checks; no packages deployed.'
} finally {
    $env:NUGET_PACKAGES = $previousCache
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
