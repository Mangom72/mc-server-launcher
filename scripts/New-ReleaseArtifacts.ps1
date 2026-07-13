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

$portableInfo = Get-Item -LiteralPath $portable
$portableHash = (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash.ToLowerInvariant()
$notes = "Introduces the MineHarbor brand, groups live plugin commands, and improves quick-command counts and documentation."
$bridgeInfo = Get-Item -LiteralPath $bridge
$bridgeHash = (Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash.ToLowerInvariant()
$metadata = [ordered]@{
    version = [string]$version.productVersion
    build = [string]$version.buildNumber
    # 이전 버전은 이 필드를 사용하므로 호환 자산 이름을 유지합니다.
    download_url = "https://github.com/Mangom72/mc-server-launcher/releases/download/$ReleaseTag/Minecraft-Server-Launcher.exe"
    primary_download_url = "https://github.com/Mangom72/mc-server-launcher/releases/download/$ReleaseTag/MineHarbor.exe"
    sha256 = $portableHash
    size = $portableInfo.Length
    release_notes = $notes
    minimum_supported_version = [string]$version.minimumSupportedVersion
	bridge = [ordered]@{
		version = [string]$version.productVersion
		protocol = 1
		minimum_minecraft = '1.13'
		maximum_minecraft = '26.2'
		download_url = "https://github.com/Mangom72/mc-server-launcher/releases/download/$ReleaseTag/MineHarbor-Command-Bridge-Paper-v$($version.productVersion).jar"
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

- 제품 이름과 실행·설치·Portable·브리지 파일 이름을 MineHarbor로 통일했습니다.
- 기존 버전의 자동 업데이트가 끊기지 않도록 호환 EXE 자산을 함께 제공합니다.
- Paper/Purpur 플러그인 명령을 `플러그인 → 플러그인 이름 → 명령어` 구조로 표시합니다.
- 빠른 명령을 `카테고리 → 기능 → 명령`의 3단 구조로 정리했습니다.
- 월드 명령은 `월드 → 날씨 → 맑음/비/천둥`, `월드 → 난이도 → 평화로움/쉬움/보통/어려움`처럼 찾을 수 있습니다.
- 이름, 설명, 계층 경로와 실제 명령어를 한 번에 검색합니다.
- `Ctrl+F`, 방향키, `Enter`, `Esc`를 이용한 키보드 조작을 지원합니다.
- 기본 Windows 스크롤바를 다크·라이트·고대비 테마에 맞는 둥근 스크롤바로 교체했습니다.
- 스크롤이 필요한 목록에만 손잡이를 표시하며 마우스 휠, 드래그와 트랙 클릭을 지원합니다.

### 빠른 명령과 실시간 자동완성

- 메인 화면에서 기본·사용자 빠른 명령과 명령 기록을 사용할 수 있습니다.
- 현재 커서와 따옴표를 인식하는 로컬 자동완성을 제공하며, 브리지가 없어도 동작합니다.
- Paper/Purpur 서버는 사용자가 서버별로 동의한 경우에만 실시간 명령 브리지를 설치합니다.
- 브리지는 무작위 세션 토큰과 임시 포트를 사용해 `127.0.0.1`에서만 통신하며 외부 포트를 열지 않습니다.
- 실시간 후보는 Paper 공개 CommandMap API를 사용하고, 실제 명령 실행은 기존 서버 콘솔 경로에서만 수행합니다.

### UX 개선

- 빠른 명령 선택창을 세 개의 둥근 카드와 현재 선택 경로, 명령 미리보기 중심으로 재구성했습니다.
- 검색 영역과 카드 배경을 자연스럽게 연결하고 선택·닫기 버튼을 오른쪽에 안정적으로 정렬했습니다.
- 서버 관리 버튼이 아이콘과 실제 글꼴에 필요한 최소 폭을 유지해 한국어·영어 문구가 잘리지 않습니다.
- 메인 작업 버튼이 창 너비에 맞게 정렬되며, 콘솔을 닫았을 때 불필요한 빈 공간을 줄였습니다.
- 라이트·다크·Windows 고대비 모드의 텍스트 대비와 키보드 포커스 표시를 개선했습니다.
- 설정 입력 오류를 관련 필드에서 바로 안내하고 불필요한 가로 스크롤과 중복 팝업을 제거했습니다.
- 서버 관리 목록의 깜빡임과 선택 위치 이동을 줄이고 로딩·비활성 상태를 더 명확하게 표시합니다.
- F5, Shift+F5, Ctrl+쉼표, Ctrl+K 단축키를 추가했습니다.

### UI 디자인 개선

- 서버 제어와 관리 도구를 시각적으로 구분하고 주요 동작의 우선순위를 명확하게 정리했습니다.
- 메인, 서버 관리, 백업, 콘텐츠 화면에 동일한 선형 벡터 아이콘 체계를 적용했습니다.
- 다크 모드에서 흰색으로 보이던 콤보박스를 테마 대응형 컨트롤로 교체했습니다.
- 설정 화면을 빠른 설정, 기본 정보, 서버 규칙으로 나누고 선택하지 않은 영역의 빈 공간을 줄였습니다.
- 선택된 프리셋에 체크 표시를 추가해 색상에만 의존하지 않도록 개선했습니다.
- 설정 화면의 가로 스크롤을 제거하고 짧은 한국어·영어 버튼 이름과 전체 설명 도움말을 적용했습니다.
- 일반 경고, 호환성 안내와 오류를 콘솔 필터와 색상으로 구분합니다.

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
