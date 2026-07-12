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

$portable = Join-Path $artifacts 'Minecraft-Server-Launcher.exe'
$zip = Join-Path $artifacts ("Minecraft-Server-Launcher-Portable-v{0}.zip" -f $version.productVersion)
$setup = Join-Path $artifacts ("Minecraft-Server-Launcher-Setup-v{0}.exe" -f $version.productVersion)
foreach ($path in @($portable, $zip, $setup)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Missing release artifact: $path" }
}

$portableInfo = Get-Item -LiteralPath $portable
$portableHash = (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash.ToLowerInvariant()
$notes = "Responsive launcher UX, accessible themes and keyboard focus, inline setup validation, stable server lists, and SHA-256 verified updates."
$metadata = [ordered]@{
    version = [string]$version.productVersion
    build = [string]$version.buildNumber
    download_url = "https://github.com/Mangom72/mc-server-launcher/releases/download/$ReleaseTag/Minecraft-Server-Launcher.exe"
    sha256 = $portableHash
    size = $portableInfo.Length
    release_notes = $notes
    minimum_supported_version = [string]$version.minimumSupportedVersion
}
$updateMetadata = Join-Path $artifacts 'update.json'
$metadataJson = $metadata | ConvertTo-Json
[IO.File]::WriteAllText($updateMetadata, $metadataJson, [Text.UTF8Encoding]::new($false))

$files = @($portable, $zip, $setup, $updateMetadata)
$sumLines = foreach ($path in $files) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($path))"
}
[IO.File]::WriteAllLines((Join-Path $artifacts 'SHA256SUMS.txt'), $sumLines, [Text.UTF8Encoding]::new($false))

$releaseNotes = @"
## Minecraft Server Launcher v$($version.productVersion)

제품 버전: **$($version.productVersion)**<br>
내부 빌드: **$($version.buildNumber)**

### 주요 변경 사항

- Portable EXE/ZIP과 Windows 설치형을 함께 제공합니다.
- 사용자 폴더, Portable 폴더 또는 사용자 지정 서버 데이터 위치를 선택할 수 있습니다.
- 제품 버전과 내부 빌드 번호를 분리해 표시하고 관리합니다.
- 자동 업데이트 파일의 크기와 SHA-256을 검증하며, 교체 실패 시 기존 실행 파일로 복구합니다.
- 최초 설정 화면의 배치와 고해상도 DPI 대응을 개선했습니다.
- GitHub Actions에서 테스트, Portable, 설치 파일, 해시와 업데이트 메타데이터를 자동 생성합니다.

### UX 개선

- 메인 작업 버튼이 창 너비에 맞게 정렬되며, 콘솔을 닫았을 때 불필요한 빈 공간을 줄였습니다.
- 라이트·다크·Windows 고대비 모드의 텍스트 대비와 키보드 포커스 표시를 개선했습니다.
- 설정 입력 오류를 관련 필드에서 바로 안내하고 불필요한 가로 스크롤과 중복 팝업을 제거했습니다.
- 서버 관리 목록의 깜빡임과 선택 위치 이동을 줄이고 로딩·비활성 상태를 더 명확하게 표시합니다.
- F5, Shift+F5, Ctrl+쉼표, Ctrl+K 단축키를 추가했습니다.

### 데이터와 설치 주의 사항

- 제거 프로그램은 사용자의 서버 프로필, 월드, 백업 등 서버 데이터를 삭제하지 않습니다.
- 이번 배포 파일은 코드 서명 인증서로 서명되지 않았습니다. Windows SmartScreen 경고가 표시될 수 있습니다.

### 알려진 제한 사항

- 외부 접속 검사는 인터넷 회선, 공유기, 방화벽, 이중 NAT 또는 CGNAT 환경에 따라 실패할 수 있습니다.
- UPnP를 지원하지 않거나 비활성화한 공유기에서는 수동 포트포워딩이 필요합니다.
- 일부 고배율 DPI 및 특수 다중 모니터 조합은 실제 장치에서 추가 확인이 필요할 수 있습니다.

전체 변경 기록은 저장소의 `CHANGELOG.md`에서 확인할 수 있습니다.
"@
[IO.File]::WriteAllText((Join-Path $artifacts 'release-notes.md'), $releaseNotes, [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    ProductVersion = $version.productVersion
    BuildNumber = $version.buildNumber
    ReleaseTag = $ReleaseTag
    Artifacts = $files + @((Join-Path $artifacts 'SHA256SUMS.txt'))
}
