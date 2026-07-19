[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactsDirectory,
    [string]$ReleaseTag
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$version = Get-Content -LiteralPath (Join-Path $projectRoot 'version.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($ReleaseTag)) { $ReleaseTag = 'v' + $version.productVersion }

$portable = Join-Path $artifacts 'MineHarbor.exe'
$legacyPortable = Join-Path $artifacts 'Minecraft-Server-Launcher.exe'
$zip = Join-Path $artifacts ("MineHarbor-Portable-v{0}.zip" -f $version.productVersion)
$setup = Join-Path $artifacts ("MineHarbor-Setup-v{0}.exe" -f $version.productVersion)
$bridge = Join-Path $artifacts ("MineHarbor-Command-Bridge-Paper-v{0}.jar" -f $version.productVersion)
foreach ($path in @($portable, $legacyPortable, $zip, $setup, $bridge)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Missing release artifact: $path" }
}

# 업데이트 창과 GitHub 릴리스가 항상 현재 제품 버전의 변경 기록을 공유하도록 CHANGELOG에서 직접 읽습니다.
$changelog = [IO.File]::ReadAllText((Join-Path $projectRoot 'CHANGELOG.md'), [Text.Encoding]::UTF8)
$escapedVersion = [Regex]::Escape([string]$version.productVersion)
$sectionMatch = [Regex]::Match($changelog, "(?ms)^## \[$escapedVersion\][^\r\n]*\r?\n(?<body>.*?)(?=^## \[|\z)")
if (!$sectionMatch.Success) { throw "CHANGELOG section is missing for version $($version.productVersion)." }
$changelogSection = $sectionMatch.Groups['body'].Value.Trim()
$koreanMatch = [Regex]::Match($changelogSection, "(?ms)(?:### Korean\r?\n)?(?<korean>.*?)(?=### English|\z)")
$englishMatch = [Regex]::Match($changelogSection, "(?ms)### English\r?\n(?<english>.*?)(?=###.*|\z)")

$koreanText = if ($koreanMatch.Success) { $koreanMatch.Groups['korean'].Value.Trim() } else { $changelogSection }
$englishText = if ($englishMatch.Success) { $englishMatch.Groups['english'].Value.Trim() } else { $null }

$koreanLines = @($koreanText -split '\r?\n' | Where-Object { $_ -match '^\s*-\s+\S' } | ForEach-Object { $_.Trim() })
if ($koreanLines.Count -eq 0) { throw "CHANGELOG section has no release notes for version $($version.productVersion)." }
$notes = $koreanLines -join "`n"

$notesEn = $null
if ($englishText) {
    $englishLines = @($englishText -split '\r?\n' | Where-Object { $_ -match '^\s*-\s+\S' } | ForEach-Object { $_.Trim() })
    if ($englishLines.Count -gt 0) {
        $notesEn = $englishLines -join "`n"
    }
}

$portableInfo = Get-Item -LiteralPath $portable
$portableHash = (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash.ToLowerInvariant()
$bridgeInfo = Get-Item -LiteralPath $bridge
$bridgeHash = (Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash.ToLowerInvariant()
$metadata = [ordered]@{
    version = [string]$version.productVersion
    build = [string]$version.buildNumber
    # 이전 버전은 이 필드를 사용하므로 호환 자산 이름을 유지합니다.
    download_url = "https://github.com/Mangom72/MineHarbor/releases/download/$ReleaseTag/Minecraft-Server-Launcher.exe"
    primary_download_url = "https://github.com/Mangom72/MineHarbor/releases/download/$ReleaseTag/MineHarbor.exe"
    sha256 = $portableHash
    size = $portableInfo.Length
    release_notes = $notes
    release_notes_en = $notesEn
    minimum_supported_version = [string]$version.minimumSupportedVersion
    bridge = [ordered]@{
        version = [string]$version.productVersion
        protocol = 1
        minimum_minecraft = '1.13'
        maximum_minecraft = '26.2'
        download_url = "https://github.com/Mangom72/MineHarbor/releases/download/$ReleaseTag/MineHarbor-Command-Bridge-Paper-v$($version.productVersion).jar"
        sha256 = $bridgeHash
        size = $bridgeInfo.Length
    }
}
$updateMetadata = Join-Path $artifacts 'update.json'
$metadataJson = $metadata | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($updateMetadata, $metadataJson, [Text.UTF8Encoding]::new($false))

$files = @($portable, $legacyPortable, $zip, $setup, $bridge, $updateMetadata)
$sumLines = foreach ($path in $files) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($path))"
}
[IO.File]::WriteAllLines((Join-Path $artifacts 'SHA256SUMS.txt'), $sumLines, [Text.UTF8Encoding]::new($false))

$releaseNotes = @"
## MineHarbor — Minecraft Server Launcher v$($version.productVersion)

제품 버전: **$($version.productVersion)**<br>
내부 빌드: **$($version.buildNumber)**

### 주요 변경 사항

$changelogSection

### 다운로드 안내

- 일반 사용자는 `MineHarbor-Setup-v$($version.productVersion).exe`를 권장합니다.
- 설치 없이 사용하려면 `MineHarbor.exe` 또는 Portable ZIP을 내려받으세요.
- 기존 런처의 자동 업데이트 호환을 위해 `Minecraft-Server-Launcher.exe`도 같은 파일로 제공합니다.
- 서버 Java 런타임은 필요한 버전을 최초 실행 때 한 번만 내려받아 검증한 뒤 캐시합니다.

### 설치 주의 사항

- 제거 프로그램은 서버 프로필, 월드와 백업 데이터를 삭제하지 않습니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.

전체 변경 기록은 저장소의 `CHANGELOG.md`에서 확인할 수 있습니다.
"@
[IO.File]::WriteAllText((Join-Path $artifacts 'release-notes.md'), $releaseNotes, [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    ProductVersion = $version.productVersion
    BuildNumber = $version.buildNumber
    ReleaseTag = $ReleaseTag
    Artifacts = $files + @((Join-Path $artifacts 'SHA256SUMS.txt'))
}
