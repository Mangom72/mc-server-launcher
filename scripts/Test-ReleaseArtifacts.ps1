[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactsDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$version = Get-Content -LiteralPath (Join-Path $projectRoot 'version.json') -Raw | ConvertFrom-Json
$names = @(
    'MineHarbor.exe',
    'Minecraft-Server-Launcher.exe',
    "MineHarbor-Portable-v$($version.productVersion).zip",
    "MineHarbor-Setup-v$($version.productVersion).exe",
    "MineHarbor-Command-Bridge-Paper-v$($version.productVersion).jar",
    'SHA256SUMS.txt',
    'update.json'
)
foreach ($name in $names) {
    $path = Join-Path $artifacts $name
    if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Release artifact is missing or empty: $name" }
}

$hashLines = Get-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt')
$expectedHashed = $names | Where-Object { $_ -ne 'SHA256SUMS.txt' }
foreach ($name in $expectedHashed) {
    $line = $hashLines | Where-Object { $_ -match ('  ' + [Regex]::Escape($name) + '$') } | Select-Object -First 1
    if (!$line) { throw "SHA256SUMS entry is missing: $name" }
    $expected = ($line -split '\s+', 2)[0]
    $actual = (Get-FileHash -LiteralPath (Join-Path $artifacts $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if (!$actual.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) { throw "SHA-256 mismatch: $name" }
}

$metadata = Get-Content -LiteralPath (Join-Path $artifacts 'update.json') -Raw | ConvertFrom-Json
if ([string]$metadata.version -ne [string]$version.productVersion -or [string]$metadata.build -ne [string]$version.buildNumber) { throw 'Launcher update metadata version mismatch.' }
$portable = Join-Path $artifacts 'MineHarbor.exe'
$legacyPortable = Join-Path $artifacts 'Minecraft-Server-Launcher.exe'
if ((Get-FileHash -LiteralPath $legacyPortable -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash) { throw 'Legacy launcher compatibility asset does not match MineHarbor.exe.' }
if ([long]$metadata.size -ne (Get-Item -LiteralPath $portable).Length) { throw 'Launcher update metadata size mismatch.' }
if (![string]::Equals([string]$metadata.sha256, (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash, [StringComparison]::OrdinalIgnoreCase)) { throw 'Launcher update metadata hash mismatch.' }
if (!([string]$metadata.download_url).EndsWith('/Minecraft-Server-Launcher.exe', [StringComparison]::OrdinalIgnoreCase)) { throw 'Legacy launcher update URL is missing.' }
if (!([string]$metadata.primary_download_url).EndsWith('/MineHarbor.exe', [StringComparison]::OrdinalIgnoreCase)) { throw 'MineHarbor primary update URL is missing.' }
$bridge = Join-Path $artifacts "MineHarbor-Command-Bridge-Paper-v$($version.productVersion).jar"
if ([int]$metadata.bridge.protocol -ne 1 -or [string]$metadata.bridge.minimum_minecraft -ne '1.13' -or [string]$metadata.bridge.maximum_minecraft -ne '26.2') { throw 'Bridge compatibility metadata mismatch.' }
if ([long]$metadata.bridge.size -ne (Get-Item -LiteralPath $bridge).Length) { throw 'Bridge metadata size mismatch.' }
if (![string]::Equals([string]$metadata.bridge.sha256, (Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash, [StringComparison]::OrdinalIgnoreCase)) { throw 'Bridge metadata hash mismatch.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$portableZip = Join-Path $artifacts "MineHarbor-Portable-v$($version.productVersion).zip"
$archive = [IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    foreach ($entry in @('MineHarbor.exe', 'README.md', 'LICENSE', 'PRIVACY.md')) {
        if (!$archive.GetEntry($entry)) { throw "Portable ZIP entry is missing: $entry" }
    }
}
finally { $archive.Dispose() }

$jar = [IO.Compression.ZipFile]::OpenRead($bridge)
try {
    if (!$jar.GetEntry('plugin.yml') -or !$jar.GetEntry('io/github/mcserverlauncher/bridge/CommandBridgePlugin.class')) { throw 'Bridge JAR entries are incomplete.' }
    foreach ($entry in $jar.Entries) {
        if ($entry.FullName.Contains('\')) { throw 'Bridge JAR contains an invalid Windows path separator.' }
        if ($entry.FullName.StartsWith('net/kyori/', [StringComparison]::OrdinalIgnoreCase) -or $entry.FullName.StartsWith('org/jetbrains/', [StringComparison]::OrdinalIgnoreCase)) { throw 'Compile-only bridge stubs leaked into the release JAR.' }
    }
}
finally { $jar.Dispose() }

$launcherVersion = (Get-Item -LiteralPath $portable).VersionInfo
if ($launcherVersion.ProductVersion.Trim() -ne [string]$version.productVersion -or $launcherVersion.FileVersion.Trim() -ne [string]$version.buildNumber) { throw 'Portable EXE version resource mismatch.' }
$setup = Join-Path $artifacts "MineHarbor-Setup-v$($version.productVersion).exe"
$setupVersion = (Get-Item -LiteralPath $setup).VersionInfo
if ($setupVersion.ProductVersion.Trim() -ne [string]$version.productVersion -or $setupVersion.FileVersion.Trim() -ne [string]$version.buildNumber) { throw 'Installer version resource mismatch.' }
Write-Host "RELEASE_ARTIFACTS_PASSED=$($names.Count)"
