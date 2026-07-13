[CmdletBinding()]
param(
    [string]$DestinationDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $projectRoot '.build\dependencies'
}
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'build-resources.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $destination | Out-Null

foreach ($name in @('paperApi', 'adventureApi', 'adventureKey')) {
    $item = $manifest.$name
    $target = Join-Path $destination $item.fileName
    $valid = Test-Path -LiteralPath $target
    if ($valid) {
        $file = Get-Item -LiteralPath $target
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        $valid = $file.Length -eq [long]$item.size -and $hash.Equals([string]$item.sha256, [StringComparison]::OrdinalIgnoreCase)
    }
    if ($valid) {
        Write-Host "Verified $($item.fileName)"
        continue
    }

    $temporary = $target + '.download'
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading $($item.fileName)"
    Invoke-WebRequest -Uri $item.url -OutFile $temporary -Headers @{ 'User-Agent' = 'MineHarbor-Build/0.3' }
    $download = Get-Item -LiteralPath $temporary
    $downloadHash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
    if ($download.Length -ne [long]$item.size -or !$downloadHash.Equals([string]$item.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        throw "Build resource verification failed: $($item.fileName)"
    }
    Move-Item -LiteralPath $temporary -Destination $target -Force
}

[pscustomobject]@{
    PaperApiJar = Join-Path $destination $manifest.paperApi.fileName
	AdventureApiJar = Join-Path $destination $manifest.adventureApi.fileName
	AdventureKeyJar = Join-Path $destination $manifest.adventureKey.fileName
}
