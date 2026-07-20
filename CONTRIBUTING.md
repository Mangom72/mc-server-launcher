# Contributing

## 개발 환경

- Windows 10 또는 Windows 11 x64
- Windows에 포함된 .NET Framework 4.x C# 컴파일러
- SDK 스타일 호환 빌드 확인 시 .NET 10 SDK
- PowerShell 5.1 이상
- 설치 프로그램 빌드 시 Inno Setup 6.7 이상

## 빌드

```powershell
.\scripts\Prepare-BuildResources.ps1
.\build.ps1
dotnet build .\MineHarbor.csproj -c Release
```

설치 프로그램까지 만들려면 다음을 실행합니다.

```powershell
.\build.ps1 -BuildInstaller -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

외부 빌드 리소스는 `build-resources.json`에 URL, 크기와 SHA-256을 고정합니다. 해시를 확인하지 않은 파일로 값을 갱신하지 마세요.

## 테스트

```powershell
.\test.ps1
```

테스트는 임시 폴더를 사용해야 하며 실제 서버 데이터, 공유기 UPnP 매핑 또는 외부 포트 설정을 변경해서는 안 됩니다.

`test.ps1`은 버전·문서 일치, Portable EXE 버전, 콘텐츠 manifest와 데이터팩 실패 경로, 자동화 실행 임대, 백업 보존, 비동기 UI 종료 및 Paper/Purpur 브리지 프로토콜을 함께 검사합니다. PR과 `main` push는 `.github/workflows/ci.yml`, 태그와 수동 릴리스는 별도 `build-release.yml`에서 검증합니다.

정식 릴리스가 게시되면 `build-release.yml`은 공개 자산 7종을 다시 내려받고, 직전 정식 버전의 실제 `ParseLauncherUpdateMetadata` 및 `DownloadLauncherUpdate` 루틴으로 새 EXE를 받습니다. 그 EXE의 크기·SHA-256·버전 리소스와 전체 회귀 테스트가 모두 통과해야 릴리스 작업이 성공합니다. 수동 재검증은 다음 명령을 사용합니다.

```powershell
.\scripts\Test-ReleaseArtifacts.ps1 -ArtifactsDirectory <공개-자산-폴더> -PublishedAssets
.\scripts\Test-PublicAutoUpdate.ps1 -SourceLauncherPath <이전-MineHarbor.exe> -UpdateMetadataPath <공개-update.json> -DestinationPath <새-다운로드-경로>
```

## 변경 원칙

- `version.json`을 제품/빌드 버전의 단일 기준으로 사용합니다.
- 사용자 데이터 이동·삭제·덮어쓰기를 자동화하지 않습니다.
- 네트워크 다운로드에는 HTTPS, 허용된 호스트, 크기와 해시 검증을 적용합니다.
- UI 문구는 한국어와 영어를 함께 갱신합니다.
- 관련 없는 대규모 포맷팅을 피하고 한 변경에는 한 목적만 담습니다.
- 콘텐츠와 자동화 설정은 `.mineharbor` 아래에 원자적으로 저장하고, 손상된 원본을 자동으로 덮어쓰지 않습니다.
- 장시간 작업은 `Task`/`async`와 `CancellationToken`을 사용하고, 닫힌 폼에 완료 콜백을 보내지 않는 테스트를 추가합니다.
- `build.ps1`과 `MineHarbor.csproj`의 명시적 소스 목록을 함께 갱신합니다.

## English

Build on Windows with PowerShell and the .NET Framework compiler. Run `scripts\Prepare-BuildResources.ps1`, `build.ps1`, and `test.ps1`; with the .NET 10 SDK installed, also run `dotnet build MineHarbor.csproj -c Release` for the SDK-style `net48` compatibility path. Inno Setup 6.7 or newer is required for installer builds. After publishing, the release workflow downloads all public assets, uses the immediately preceding stable launcher's real update parser and download routine, and runs the full regression suite against that downloaded EXE. Keep `version.json` as the single version source, keep `build.ps1` and project source lists synchronized, never mutate real server/router data in tests, preserve corrupt manifests instead of overwriting them, cancel long-running UI work safely, verify downloads, and update Korean and English UI/documentation together.
