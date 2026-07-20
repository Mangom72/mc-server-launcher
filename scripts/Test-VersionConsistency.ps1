[CmdletBinding()]
param(
    [string]$LauncherPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionPath = Join-Path $projectRoot 'version.json'
$version = Get-Content -LiteralPath $versionPath -Raw | ConvertFrom-Json

if ([string]$version.productVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'version.json productVersion must use MAJOR.MINOR.PATCH.' }
if ([string]$version.buildNumber -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'version.json buildNumber must have four numeric components.' }
if ([string]$version.minimumSupportedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'version.json minimumSupportedVersion must use MAJOR.MINOR.PATCH.' }

& (Join-Path $projectRoot 'scripts\Generate-VersionInfo.ps1') | Out-Null
$generated = Get-Content -LiteralPath (Join-Path $projectRoot 'obj\GeneratedVersionInfo.cs') -Raw
if ($generated -notmatch [Regex]::Escape('ProductVersion = "' + $version.productVersion + '"')) { throw 'GeneratedVersionInfo product version mismatch.' }
if ($generated -notmatch [Regex]::Escape('BuildNumber = "' + $version.buildNumber + '"')) { throw 'GeneratedVersionInfo build number mismatch.' }

$readme = Get-Content -LiteralPath (Join-Path $projectRoot 'README.md') -Raw
$changeLog = Get-Content -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Raw
if ($readme -notmatch [Regex]::Escape('v' + $version.productVersion)) { throw 'README.md does not mention the current product version.' }
if ($changeLog -notmatch ('(?m)^## \[' + [Regex]::Escape([string]$version.productVersion) + '\]')) { throw 'CHANGELOG.md has no entry for the current product version.' }

$build = Get-Content -LiteralPath (Join-Path $projectRoot 'build.ps1') -Raw
$project = Get-Content -LiteralPath (Join-Path $projectRoot 'MineHarbor.csproj') -Raw
foreach ($source in @('ContentManagementServices.cs', 'ContentManagementUi.cs', 'ServerAutomation.cs', 'ServerManagementFeatures.cs')) {
    if ($build -notmatch [Regex]::Escape($source)) { throw "build.ps1 source list is missing $source." }
    if ($project -notmatch [Regex]::Escape($source)) { throw "MineHarbor.csproj source list is missing $source." }
}

$resources = Get-Content -LiteralPath (Join-Path $projectRoot 'build-resources.json') -Raw | ConvertFrom-Json
foreach ($name in @('java', 'paperApi', 'adventureApi', 'adventureKey')) {
    $item = $resources.$name
    $uri = $null
    if ($null -eq $item -or [string]::IsNullOrWhiteSpace([string]$item.fileName) -or ![Uri]::TryCreate([string]$item.url, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') { throw "Build resource URL is invalid: $name" }
    if ([string]$item.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or [long]$item.size -le 0) { throw "Build resource size or SHA-256 is invalid: $name" }
}
$paperUri = [Uri]$resources.paperApi.url
if ($paperUri.Host -ne 'artifactory.papermc.io' -or !$paperUri.AbsolutePath.StartsWith('/artifactory/universe/', [StringComparison]::Ordinal)) { throw 'Paper API must use the official immutable Artifactory artifact path.' }

if (![string]::IsNullOrWhiteSpace($LauncherPath)) {
    $launcher = [IO.Path]::GetFullPath($LauncherPath)
    if (!(Test-Path -LiteralPath $launcher)) { throw "Launcher not found: $launcher" }
    $info = (Get-Item -LiteralPath $launcher).VersionInfo
    if ($info.ProductVersion.Trim() -ne [string]$version.productVersion) { throw 'Portable EXE product version mismatch.' }
    if ($info.FileVersion.Trim() -ne [string]$version.buildNumber) { throw 'Portable EXE build number mismatch.' }
}

Write-Host 'VERSION_CONSISTENCY_OK'
