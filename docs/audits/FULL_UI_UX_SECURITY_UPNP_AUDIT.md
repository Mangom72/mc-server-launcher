# MineHarbor 전체 UI·UX·보안·UPnP 감사

> 감사 기준: `codex/audit-ui-ux-security-upnp` / `origin/main` `c638cc1`
>
> 감사일: 2026-07-18 (Asia/Seoul)
>
> 성격: 제품 소스 수정 전 정적 감사 및 안전한 재현 계획

## 1. 감사 범위

- Git 추적 파일 71개 전체를 목록화하고 문서, 제품 코드, 테스트, 빌드·릴리스, 설치, 다국어 및 설정 영역으로 분류했다.
- C# 23개, PowerShell 13개, GitHub Actions 워크플로 1개를 우선 코드 감사 대상으로 삼았다.
- 실제 공유기, UPnP 매핑, 외부 포트, 방화벽, 공개 서버 및 `%LOCALAPPDATA%\MineHarbor`에는 접근하지 않았다.
- 실제 UI 실행은 사용자 데이터 접근 가능성 때문에 하지 않았고, WinForms 생성 테스트와 정적 레이아웃·접근성 검사를 근거로 판단했다.
- 발견 사항은 코드로 입증된 동작과 장치 의존 위험을 분리했다. `NEEDS_DEVICE_TEST`는 실제 공유기에서 확인하기 전 결함으로 단정하지 않는다.

## 2. 조사한 파일

| 분류 | 파일 |
| --- | --- |
| 진입 문서·정책 | `AGENTS.md`, `.agents/AGENTS.md`, `CONTRIBUTING.md`, `README.md`, `SECURITY.md`, `PRIVACY.md`, `CHANGELOG.md`, `docs/ai/*.md` |
| 핵심 런처·UI | `decompiled/Launcher.decompiled.cs`, `ModernLauncherGui.cs`, `ManagedServerDashboard.cs`, `ModernDialogs.cs`, `CustomControls.cs`, `RoundedProgressBar.cs`, `ThemePalette.cs`, `Localization.cs` |
| UPnP·네트워크 | `UpnpExternalAccess.cs`, `UpnpCore.cs`, `NetworkAndPlayerTools.cs`, `test_upnp.cs`, `tests/Launcher.Tests.cs` |
| 다운로드·업데이트 | `RuntimeCompatibility.cs`, `ContentAndDiagnostics.cs`, `QuickCommandsAndBridge.cs`, `build-resources.json` |
| 데이터·백업 | `StorageConfiguration.cs`, `BackupAndProfileTools.cs`, `ServerTrash.cs` |
| 프로세스·브리지 | `ChildProcessTracker.cs`, `QuickCommandUi.cs`, `QuickCommandPickerUi.cs`, `bridge/paper/**`, `admin-plugin-src/**` |
| 빌드·릴리스·설치 | `build.ps1`, `test.ps1`, `scripts/*.ps1`, `.github/workflows/build-release.yml`, `installer/MineHarbor.iss`, `app.manifest`, `version.json` |
| 유지보수 보조물 | `fix_*.py`, `update_changelog.py`, `test_csc.ps1`, `.vscode/tasks.json`, 나머지 추적 문서와 설정 |

생성물인 `artifacts`, `obj`, `.build`, `bin`, `TestResults`와 EXE·ZIP·JAR·캐시는 감사 대상 파일 수에서 제외했다.

## 3. 실행한 명령

```powershell
git rev-parse --show-toplevel
git branch --show-current
git status --porcelain
git fetch origin --prune
git switch -c codex/audit-ui-ux-security-upnp origin/main
git ls-files | Sort-Object
.\scripts\Prepare-BuildResources.ps1
.\build.ps1
.\test.ps1 -LauncherPath .\artifacts\MineHarbor.exe
rg -n --hidden <UPnP·네트워크·보안·UI·경로·프로세스 패턴>
```

`Prepare-BuildResources.ps1`과 빌드는 `.build`, `artifacts`, `obj`에만 생성물을 만들었다. 테스트는 임시 폴더와 루프백 모의 서버만 사용했다.

## 4. 기준 빌드·테스트 결과

| 단계 | 결과 | 종료 코드 | 시간 | 비고 |
| --- | --- | ---: | ---: | --- |
| 리소스 준비 | 성공 | 0 | 약 20.9초 | Paper API, Adventure API, 고정 JDK를 크기·SHA-256으로 검증 |
| 빌드 | 실패 | 1 | 29.650초 | C# 컴파일과 브리지 빌드 후 `sign-build.ps1`의 자체 서명 인증서 생성에서 `NTE_PERM` |
| 테스트 | 실패 | 1 | 11.818초 | 서명 전 생성된 EXE 사용. `TestSocketUpnpLocalServer`의 `HttpListener` 생성에서 `PlatformNotSupportedException` |

- 테스트 파일은 22개 상위 테스트 그룹을 순서대로 호출하고 `PASSED` 카운터는 최대 21회 증가하도록 작성되어 있다. 이번 실행은 최종 `PASSED=`를 출력하기 전에 중단되어 통과 개수를 확정할 수 없다.
- 권한 상승 빌드는 사용자 인증서 저장소에 인증서를 영구 추가하고 고정 비밀번호 PFX를 만들기 때문에 안전 검토에서 거부되었다.
- 테스트가 다루지 못한 중요 영역: 실제 COM/IGD 동작, 공유기별 설명 문자열 변형, Add 응답 유실, 삭제 전 소유권 변경, 다중 프로세스 동시성, 방화벽 우선순위, CGNAT·이중 NAT, 125~200% 실제 DPI 및 다중 모니터.

## 5. 시스템 구조 요약

- 기본 서버 프로세스는 `decompiled/Launcher.decompiled.cs`에서 실행되고, `ModernLauncherGui.cs`가 메인 UI와 전역 프로세스 상태를 관리한다.
- 서버 시작 후 별도 STA 백그라운드 스레드가 `RunExternalAccessPipeline`을 실행한다.
- 자동 매핑 생성·검증·정리는 `UpnpExternalAccess.cs`의 Windows COM `HNetCfg.NATUPnP` 경로를 사용한다.
- 네트워크 도구의 수동 “UPnP 정리”는 `UpnpCore.cs`의 소켓/SOAP 경로와 `%LOCALAPPDATA%` TSV 기록을 사용한다. 두 구현의 소유권 상태는 연결되어 있지 않다.
- 다운로드는 공급자별 검증 수준이 다르다. Java·Paper·Modrinth·런처 업데이트는 강한 해시 검증이 있으나 Forge 설치 파일과 로컬 릴리스 도구에는 공백이 있다.
- 프로필, 백업, 휴지통은 대부분 루트 경계와 재분석 지점을 검사하며, 설정 저장은 임시 파일 후 교체 패턴을 사용한다.
- 명령 브리지는 루프백 바인딩, 256비트 임의 토큰, 고정 시간 비교, 줄 길이 및 속도 제한을 적용한다.

## 6. UPnP 상태 흐름

```text
서버 프로세스 시작
→ STA 백그라운드 스레드 생성
→ 로컬 TCP 리스너 최대 3분 대기
→ portchecker.io 공인 IP 조회 및 외부 TCP 검사(초기 최대 3회)
→ 검사 완료·닫힘일 때만 HNetCfg.NATUPnP COM 생성
→ StaticPortMappingCollection 검색(최대 3회)
→ TCP 기존 매핑 확인 또는 Add(최대 3회)
→ enable-query=true이면 UDP 확인 또는 Add(최대 3회)
→ 컬렉션 가시성 검사(최대 3회)
→ 외부 TCP 재검사(최대 5회)
→ 서버 종료 신호를 무기한 대기
→ 현재 컬렉션에서 내부 IP·내부 포트·설명 재검증
→ 이번 시도에서 기록한 TCP/UDP만 Remove
→ Collection·NAT RCW FinalRelease
```

| 단계 | 스레드·동기성 | 취소·제한 | 실패 상태와 정리 |
| --- | --- | --- | --- |
| 로컬 포트 대기 | STA 작업 스레드, 동기 | 종료 이벤트, 3분 | 매핑 없음, 즉시 종료 |
| 외부 검사 | STA 작업 스레드, 동기 HTTP | 요청당 15초이나 진행 중 취소 불가 | 최초 서비스 오류는 UPnP 미실행 |
| COM 검색·Add | 같은 STA 스레드 | 재시도 간 종료 확인, COM 호출 자체 제한 없음 | 기록된 부분 성공은 `finally`까지 보존 |
| TCP 성공·UDP 실패 | 같은 STA 스레드 | UDP 실패 후 TCP 외부 검사 계속 | TCP는 서버 종료 때 삭제, UDP 실패는 성공 상태에 가려질 수 있음 |
| 서버 유지 | STA 작업 스레드 | `serverStopped.WaitOne()` 무기한 | COM Collection을 서버 수명 동안 보유 |
| 종료 정리 | 같은 STA 스레드 | 호출자 `Join(45000)`만 존재, COM Remove 취소 불가 | 45초 초과 시 스레드는 백그라운드에 남고 삭제 실패 알림 |

## 7. 핵심 발견 사항 요약

| ID | 분야 | 심각도 | 분류 | 신뢰도 | 관련 파일 | 요약 |
| --- | --- | --- | --- | --- | --- | --- |
| UPNP-001 | 삭제 안전성 | P0 | HIGHLY_LIKELY | 높음 | `UpnpCore.cs` | 오래된 TSV만 믿고 현재 소유권 확인 없이 DeletePortMapping |
| UPNP-002 | COM 안정성 | P1 | CONFIRMED | 높음 | `UpnpExternalAccess.cs` | COM 호출에 시간 제한·취소가 없어 종료 정리가 무기한 정지 가능 |
| UPNP-003 | 생성·복구 | P1 | CONFIRMED | 높음 | `UpnpExternalAccess.cs`, `UpnpCore.cs` | COM 생성 기록과 소켓 정리 기록이 단절되어 비정상 종료 복구 불가 |
| SEC-001 | 공급망 | P1 | CONFIRMED | 높음 | `Launcher.decompiled.cs` | Forge 설치 JAR을 해시 없이 받고 자동 리디렉션 최종 호스트 확인 없이 실행 |
| SEC-002 | 백업 복원 | P1 | CONFIRMED | 높음 | `BackupAndProfileTools.cs` | manifest에 없는 파일도 staging에 추출·복원 가능 |
| SEC-003 | 릴리스 도구 | P1 | CONFIRMED | 높음 | `Publish-LocalRelease.ps1` | Inno Setup을 해시·서명 검사 없이 다운로드 후 실행 |
| TEST-001 | 안전 회귀 | P1 | CONFIRMED | 높음 | `tests/Launcher.Tests.cs` | 소유권 테스트가 없는 필드를 찾고 조용히 건너뛰며 가짜 COM 컬렉션도 미사용 |
| UPNP-004 | 부분 성공 | P2 | CONFIRMED | 높음 | `UpnpExternalAccess.cs` | TCP 성공·UDP 실패를 TCP 재검사 성공만으로 “UPnP 성공” 표시 가능 |
| UPNP-005 | 외부 검사 | P2 | CONFIRMED | 높음 | `UpnpExternalAccess.cs`, `Launcher.decompiled.cs` | 단일 서비스, 무제한 응답, 재검사 오류를 “접속 실패”로 표시 |
| UPNP-006 | 방화벽 | P2 | CONFIRMED | 높음 | `UpnpExternalAccess.cs` | 프로필·프로그램·주소·Block 우선순위를 무시한 허용 판정 |
| UPNP-007 | 동시성 | P2 | CONFIRMED | 중간 | `ModernLauncherGui.cs`, `UpnpExternalAccess.cs` | 재검사 중복 실행과 전역 상태 덮어쓰기 가능 |
| UPNP-008 | 주소 판정 | P2 | CONFIRMED | 중간 | `UpnpExternalAccess.cs`, `Launcher.decompiled.cs` | IPv4 WAN과 IPv6 공인 주소 비교 시 CGNAT 오탐, VPN 어댑터 선택 위험 |
| UPNP-009 | COM 자원 | P2 | DESIGN_RISK | 중간 | `UpnpExternalAccess.cs` | COM foreach 열거자 RCW의 명시적 해제 부재와 FinalRelease 사용 |
| UPNP-010 | 설명 변형 | P2 | NEEDS_DEVICE_TEST | 중간 | `UpnpExternalAccess.cs` | 공유기가 설명을 자르면 소유권 회수·삭제가 실패해 고아 매핑 가능 |
| UI-001 | 응답성·상태 | P2 | CONFIRMED | 높음 | `NetworkAndPlayerTools.cs`, `UpnpExternalAccess.cs` | UI 클릭 핸들러에서 네트워크 Task.Wait, 실패도 “0개 정리”로 표시 |
| UI-002 | 위험 명령 | P2 | CONFIRMED | 높음 | `ModernLauncherGui.cs`, `ManagedServerDashboard.cs` | 직접 콘솔 경로가 위험 명령 확인 로직을 우회 |
| DATA-001 | 복원 안정성 | P2 | CONFIRMED | 높음 | `BackupAndProfileTools.cs` | ZIP 항목 수·총 해제 크기 제한 부재 |
| SEC-004 | 로컬 서명 | P2 | CONFIRMED | 높음 | `sign-build.ps1`, `build.ps1` | 일반 빌드가 고정 비밀번호 PFX와 영구 인증서 생성 시도 |
| DOC-001 | 문서 UX | P3 | CONFIRMED | 높음 | `README.md`, `version.json` | README 버전이 1.4.0, 실제 기준은 1.5.18 |
| MAINT-001 | 유지보수 | P3 | DESIGN_RISK | 높음 | `fix_*.py`, `test_upnp.cs` | 일회성 소스 재작성 스크립트와 현재 API에 맞지 않는 위험한 테스트 잔존 |
| UPNP-011 | 미완성 구현 | P3 | DESIGN_RISK | 높음 | `UpnpCore.cs` | 소켓 `MapPortsAsync`가 실제 매핑 없이 성공을 반환하는 TODO 상태 |

## 8. P0 발견 사항

### [UPNP-001] 오래된 소유권 기록이 현재의 다른 매핑을 삭제할 수 있음

- 심각도: P0 Critical
- 분류: HIGHLY_LIKELY
- 신뢰도: 높음
- 관련 파일과 줄: `UpnpCore.cs:212-252`, `UpnpCore.cs:439-490`, `UpnpExternalAccess.cs:641-649`
- 관련 함수: `CleanupMappingsAsync`, `LoadMappings`, `ClearAllMineHarborUpnpMappings`
- 실제 동작: TSV의 `ControlUrl`, 외부 포트와 프로토콜만으로 `DeletePortMapping`을 전송한다. 삭제 직전에 현재 내부 IP·내부 포트·설명을 조회하지 않는다.
- 기대 동작: 현재 매핑의 모든 소유권 속성이 현재 실행 또는 검증된 고아 기록과 정확히 일치할 때만 삭제해야 한다.
- 발생 조건: 과거 기록이 남은 뒤 같은 외부 포트·프로토콜을 사용자나 다른 프로그램이 재사용하고 사용자가 “UPnP 정리”를 누르는 경우.
- 사용자 영향: 사용자가 만든 포트포워딩이 예고 없이 사라질 수 있다.
- 보안 또는 데이터 영향: 핵심 불변 조건 위반. 실제 사용자 네트워크 설정 삭제 가능성.
- 근본 원인: 소유권 기록을 권위 있는 현재 상태로 간주하고 삭제 전 재검증 단계를 생략했다.
- 재현 방법: 실제 공유기 대신 가짜 SOAP 서버에 오래된 TSV를 제공하고, 현재 매핑 조회 결과가 다른 내부 대상·설명이라고 구성한 뒤 삭제 요청이 전송되는지 확인한다.
- 권장 수정: “전체 정리”를 비활성화한 뒤 COM/소켓 공통 소유권 모델을 만들고 `GetGenericPortMappingEntry` 상당 조회로 5개 속성을 재검증한다. 기록 파일도 원자적·세션별로 관리한다.
- 필요한 회귀 테스트: 기존 사용자 매핑 보호, 포트 재사용, 설명 변경, 내부 IP 변경, TCP/UDP 독립 소유권, 변조된 TSV.
- 수정 시 위험: 너무 엄격하면 실제 고아 매핑을 놓칠 수 있으므로 삭제보다 보존을 우선해야 한다.

## 9. P1 발견 사항

### [UPNP-002] COM 검색·추가·삭제가 시간 제한과 취소를 보장하지 않음

- 심각도: P1 High
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `UpnpExternalAccess.cs:342-388`, `UpnpExternalAccess.cs:392-452`, `UpnpExternalAccess.cs:602-638`, `Launcher.decompiled.cs:3399-3427`
- 관련 함수: `TryDiscoverUpnpCollection`, `TryAddSingleUpnpMapping`, `DeleteCreatedUpnpMappings`, 서버 실행 종료 경로
- 실제 동작: 재시도 사이에는 종료 이벤트를 확인하지만 COM 호출 자체는 동기이며 제한 시간이 없다. 호출자는 45초 후 경고만 하고 스레드를 종료시키지 못한다.
- 기대 동작: 각 장치 작업에 제한 시간과 취소 가능한 경계가 있고 종료 시 결과가 확정돼야 한다.
- 발생 조건: 공유기 COM 구현이 응답하지 않거나 네트워크 전환·종료와 겹치는 경우.
- 사용자 영향: 종료 지연, 정리 실패, 백그라운드 스레드 잔존, 포트 매핑 잔류.
- 보안 또는 데이터 영향: 의도보다 오래 외부 포트가 열릴 수 있다.
- 근본 원인: 취소 불가능한 COM 객체를 서버 수명 전체에 보유하는 설계.
- 재현 방법: COM 추상화가 호출을 완료하지 않는 가짜 장치를 제공하고 서버 종료 후 45초 상태를 검증한다.
- 권장 수정: COM 전용 STA 작업자와 명시적 상태 머신을 만들고 장치 작업 제한 시간, 결과 채널, 종료 시 안전한 포기 정책을 둔다.
- 필요한 회귀 테스트: 검색·Add·Remove 각각 무응답, 종료 동시성, 네트워크 전환.
- 수정 시 위험: COM 스레드를 강제 중단하면 RCW가 손상될 수 있으므로 프로세스 격리 또는 요청 단위 작업자 검토가 필요하다.

### [UPNP-003] 현재 COM 매핑은 비정상 종료 복구 기록에 저장되지 않음

- 심각도: P1 High
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `UpnpExternalAccess.cs:25-43`, `UpnpExternalAccess.cs:504-520`, `UpnpCore.cs:194-252`, `UpnpCore.cs:426-490`
- 관련 함수: `RecordCreatedUpnpMapping`, `HandleCrashRecoveryAsync`, `CleanupMappingsAsync`
- 실제 동작: COM 매핑은 메모리 `attempt.Created`에만 기록된다. TSV는 소켓 구현만 읽고 쓰며 현재 COM 경로는 저장하지 않는다.
- 기대 동작: 비정상 종료 후에도 안전하게 검증 가능한 최소 소유권 기록이 남고 다음 실행에서 복구돼야 한다.
- 발생 조건: 매핑 생성 후 프로세스 강제 종료·전원 종료·크래시.
- 사용자 영향: 공유기에 고아 매핑이 무기한 남고 “UPnP 정리” 버튼도 이를 찾지 못한다.
- 보안 또는 데이터 영향: 불필요한 외부 노출 지속.
- 근본 원인: 소켓 방식에서 COM 방식으로 롤백하면서 생성과 복구 저장소를 통합하지 않았다.
- 재현 방법: 가짜 COM Add 성공 후 프로세스 종료를 모사하고 다음 시작에서 복구 후보가 0개인지 확인한다.
- 권장 수정: 구현 독립적인 소유권 저장소와 원자적 상태(`pending-add`, `owned`, `pending-delete`)를 정의한다.
- 필요한 회귀 테스트: Add 성공 직후 크래시, TCP만 성공 후 크래시, 삭제 중 크래시.
- 수정 시 위험: 기록만으로 삭제하지 말고 UPNP-001의 재검증을 반드시 결합해야 한다.

### [SEC-001] Forge 설치 파일이 강한 무결성 검증 없이 실행됨

- 심각도: P1 High
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `Launcher.decompiled.cs:2477-2488`, `Launcher.decompiled.cs:2670-2705`, `Launcher.decompiled.cs:2605-2615`
- 관련 함수: Forge 준비 경로, `DownloadFileWithUserAgent`, `RunForgeInstaller`
- 실제 동작: 최초 URL만 HTTPS 허용 호스트인지 확인하고 자동 리디렉션 후 최종 호스트를 확인하지 않으며, Forge installer JAR을 SHA-256 없이 곧바로 `java -jar`로 실행한다.
- 기대 동작: 최종 응답 호스트·크기·게시된 해시 또는 신뢰 가능한 서명을 검증한 후 실행해야 한다.
- 발생 조건: 공급자 계정·배포 경로 침해, 잘못된 리디렉션, 프록시·DNS/TLS 신뢰 체인 오염.
- 사용자 영향: 런처 권한으로 임의 코드 실행 가능.
- 보안 또는 데이터 영향: 공급망 원격 코드 실행 위험.
- 근본 원인: 공급자별 검증 정책 불일치.
- 재현 방법: 모의 HTTPS 계층에서 허용 URL이 다른 호스트로 리디렉션되도록 하고 최종 파일이 실행 경로에 도달하는지 검증한다.
- 권장 수정: 자동 리디렉션을 끄고 단계별 호스트 허용 목록을 검사하며 Forge가 제공하는 체크섬을 독립 경로로 검증한다. 검증 수단이 없으면 자동 실행하지 않는다.
- 필요한 회귀 테스트: 교차 호스트 리디렉션, 크기 초과, 해시 불일치, 부분 다운로드.
- 수정 시 위험: 공급자의 정상 CDN 호스트 변화를 허용 목록에 반영해야 한다.

### [SEC-002] 백업 manifest에 없는 파일도 복원됨

- 심각도: P1 High
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `BackupAndProfileTools.cs:941-995`, `BackupAndProfileTools.cs:1111-1131`
- 관련 함수: `VerifyComprehensiveBackup`, `ExtractComprehensiveBackup`
- 실제 동작: 검증은 manifest 항목만 검사하지만 추출은 `profile/` 아래 모든 파일을 복사한다. 추출 뒤에도 manifest 항목만 재검증한다.
- 기대 동작: archive의 파일 집합과 manifest의 파일 집합이 정확히 같아야 한다.
- 발생 조건: 가져온 ZIP에 정상 manifest와 함께 manifest 밖의 플러그인 JAR·설정·스크립트가 포함된 경우.
- 사용자 영향: 검증 통과 메시지와 달리 숨은 파일이 서버에 복원된다.
- 보안 또는 데이터 영향: 다음 서버 시작 시 악성 플러그인·모드 실행 가능.
- 근본 원인: 포함 관계만 검증하고 집합 동일성을 검증하지 않았다.
- 재현 방법: 정상 백업에 `profile/plugins/unlisted.jar`를 추가하되 manifest는 그대로 두고 복원 staging에 파일이 생기는지 확인한다.
- 권장 수정: 정규화된 경로 집합을 양방향 비교하고 manifest 밖 파일·중복·대소문자 충돌을 거부한다.
- 필요한 회귀 테스트: 미등재 파일, 중복 경로, 대소문자 충돌, 디렉터리 항목.
- 수정 시 위험: 과거 백업 형식의 의도적 부가 파일과 호환성 정책이 필요하다.

### [SEC-003] 로컬 릴리스가 검증하지 않은 Inno Setup 설치 파일을 실행함

- 심각도: P1 High
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `scripts/Publish-LocalRelease.ps1:16-31`, `.github/workflows/build-release.yml:70-82`
- 관련 함수: 로컬 Inno 설치 단계
- 실제 동작: CI는 SHA-256과 Authenticode를 확인하지만 로컬 릴리스 스크립트는 같은 EXE를 확인 없이 다운로드하고 실행한다.
- 기대 동작: 로컬과 CI가 동일한 고정 해시·서명 정책을 사용해야 한다.
- 발생 조건: 다운로드 자산·계정·전송 경로가 침해되거나 파일이 바뀐 경우.
- 사용자 영향: 릴리스 담당자 PC에서 임의 설치 프로그램 실행.
- 보안 또는 데이터 영향: 공급망 코드 실행.
- 근본 원인: 검증 로직이 CI에만 중복 구현돼 있다.
- 재현 방법: 모의 다운로드 파일의 해시가 고정값과 다를 때 로컬 스크립트가 실행 단계로 진입하는지 검증한다.
- 권장 수정: 검증 코드를 공용 스크립트로 추출하고 해시·서명 확인 실패 시 실행 금지.
- 필요한 회귀 테스트: 해시 불일치, 서명 없음·만료·잘못된 게시자.
- 수정 시 위험: 인증서 갱신 시 게시자·체인 정책 유지가 필요하다.

### [TEST-001] UPnP 소유권 안전 테스트가 실질적으로 실행되지 않음

- 심각도: P1 High
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `tests/Launcher.Tests.cs:625-668`, `tests/Launcher.Tests.cs:956-997`, `UpnpCore.cs:426-490`
- 관련 함수: `TestUpnpOwnershipRules`
- 실제 동작: 테스트는 존재하지 않는 `RegistryFilePath` 필드가 있을 때만 검증하고, 없으면 그대로 `Pass()`한다. 정의된 `FakeMappingCollection`은 어느 테스트에서도 생성되지 않는다.
- 기대 동작: 테스트 지점이 사라지면 실패하고, 실제 COM 소유권·부분 성공·삭제 보호 경로를 호출해야 한다.
- 발생 조건: 현재 모든 테스트 실행.
- 사용자 영향: 안전 회귀가 통과로 오인될 수 있다.
- 보안 또는 데이터 영향: UPNP-001~003 같은 결함의 릴리스 차단 실패.
- 근본 원인: 리팩터링 후 반사 기반 테스트 계약이 갱신되지 않았다.
- 재현 방법: `RegistryFilePath`가 null임과 Fake 클래스 참조가 정의부뿐임을 정적 검사한다.
- 권장 수정: 반사 우회 대신 COM 추상화와 강타입 테스트를 도입하고, 필수 훅 부재를 즉시 실패로 처리한다.
- 필요한 회귀 테스트: 사용자 매핑 보호, Add 응답 유실, TCP/UDP 부분 성공, 소유권 변경, 동시성.
- 수정 시 위험: 실제 공유기 없이 동작하도록 모든 외부 효과를 가짜 구현으로 격리해야 한다.

## 10. P2 발견 사항

### [UPNP-004] UDP 매핑 실패가 전체 성공 상태에 가려짐

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `UpnpExternalAccess.cs:318-333`, `UpnpExternalAccess.cs:132-156`
- 관련 함수: `TryCreateUpnpMappings`, `RunExternalAccessPipeline`
- 실제 동작: TCP 성공 후 UDP 실패·충돌이면 TCP 외부 검사만 계속하고, TCP가 열리면 “UPnP 매핑 성공”을 표시한다.
- 기대 동작: TCP 서버 접속과 Query UDP 결과를 별도 상태로 표시해야 한다.
- 발생 조건: `enable-query=true`이고 UDP Add가 실패하는 경우.
- 사용자 영향: Query 기능도 성공한 것으로 오해한다.
- 보안 또는 데이터 영향: 없음. 운영 진단 오류.
- 근본 원인: 성공 상태가 `Created.Count > 0` 하나로 합쳐져 있다.
- 재현 방법: 가짜 컬렉션에서 TCP Add 성공·UDP 충돌을 반환하고 최종 상태를 검증한다.
- 권장 수정: 프로토콜별 결과 모델과 부분 성공 UI를 사용한다.
- 필요한 회귀 테스트: TCP 성공/UDP 실패·충돌·취소.
- 수정 시 위험: 기존 TCP 성공 사용자에게 전체 실패로 표시하지 않아야 한다.

### [UPNP-005] 외부 검사 오류와 실제 폐쇄 상태가 일부 UI에서 혼동됨

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `UpnpExternalAccess.cs:223-292`, `UpnpExternalAccess.cs:187-203`, `Launcher.decompiled.cs:3697-3716`
- 관련 함수: `CheckExternalPort`, `RecheckExternalReachabilityOnly`, `DownloadText`
- 실제 동작: 자동 초기 검사는 서비스 오류 시 UPnP를 막아 안전하지만, 수동 재검사는 `CheckCompleted=false`도 “외부 접속 실패”로 표시한다. 응답 본문 크기와 최종 리디렉션 호스트도 제한하지 않는다.
- 기대 동작: `열림`, `닫힘`, `검사 불가`를 분리하고 서비스 응답을 제한해야 한다.
- 발생 조건: portchecker.io DNS/TLS/속도 제한/프록시/잘못된 응답/리디렉션.
- 사용자 영향: 불필요한 공유기 설정 변경이나 잘못된 장애 판단.
- 보안 또는 데이터 영향: 리디렉션 시 IP·포트 검사 요청이 다른 호스트로 전달될 수 있다.
- 근본 원인: 단일 서비스와 2상태 UI.
- 재현 방법: 모의 서비스에서 timeout, 302, 큰 gzip, 잘못된 bool을 반환한다.
- 권장 수정: 외부 검사 인터페이스, 엄격한 응답 제한·리디렉션 정책, 3상태 UI를 도입한다.
- 필요한 회귀 테스트: DNS/TLS/429/5xx/redirect/IPv4/IPv6/취소.
- 수정 시 위험: 다중 서비스 사용 시 개인정보 전송 범위가 늘 수 있어 명시적 고지가 필요하다.

### [UPNP-006] 방화벽 허용 규칙 판정이 실제 유효성을 과대평가함

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `UpnpExternalAccess.cs:676-731`
- 관련 함수: `HasLikelyWindowsFirewallAllowRule`
- 실제 동작: Enabled·Inbound·Allow·Protocol·LocalPorts만 본다. 활성 프로필, 프로그램(Java), 서비스, 로컬/원격 주소, 인터페이스, Block 규칙 우선순위를 무시한다.
- 기대 동작: 실제 서버 프로세스와 현재 네트워크 프로필에 적용되는 유효 규칙을 판정하거나 “규칙 후보”라고 표현해야 한다.
- 발생 조건: 다른 프로그램용 허용 규칙, 비활성 프로필, 더 강한 Block 규칙이 있는 경우.
- 사용자 영향: 방화벽이 정상이라고 잘못 안내한다.
- 보안 또는 데이터 영향: 사용자가 과도하게 넓은 방화벽 규칙을 추가할 가능성.
- 근본 원인: 단순 휴리스틱을 확정적 상태처럼 사용.
- 재현 방법: 가짜 정책에 포트는 같지만 프로그램·프로필이 다른 규칙과 Block 규칙을 제공한다.
- 권장 수정: 판정 요소를 확장하고 불확실 상태를 유지한다.
- 필요한 회귀 테스트: 프로필·프로그램·Block·포트 범위·모든 프로그램 규칙.
- 수정 시 위험: Windows 방화벽 규칙 병합·우선순위가 복잡하므로 보수적으로 표시해야 한다.

### [UPNP-007] 수동 외부 재검사를 중복 실행할 수 있음

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 중간
- 관련 파일과 줄: `ModernLauncherGui.cs:4134-4166`, `UpnpExternalAccess.cs:187-203`, `UpnpExternalAccess.cs:827-839`
- 관련 함수: 네트워크 창 recheck delegate, `RecheckExternalReachabilityOnly`, `ReportExternalAccessStatus`
- 실제 동작: 클릭마다 새 스레드를 시작하며 실행 중 버튼 비활성화·취소·세대 번호가 없다. 완료 순서에 따라 전역 주소·알림·로딩 상태가 덮인다.
- 기대 동작: 서버 프로필별 단일 검사와 최신 요청만 UI 반영.
- 발생 조건: 재검사 버튼 연속 클릭, 자동 검사와 수동 검사 중첩.
- 사용자 영향: 오래된 결과가 최신 결과를 덮고 로딩 표시가 먼저 끝날 수 있다.
- 보안 또는 데이터 영향: 없음.
- 근본 원인: 요청 수명과 UI 상태의 소유자가 없다.
- 재현 방법: 첫 가짜 요청은 느린 성공, 둘째는 빠른 실패로 구성해 최종 UI를 검증한다.
- 권장 수정: 요청 ID와 CancellationToken을 프로필별로 관리하고 버튼을 진행 중 비활성화한다.
- 필요한 회귀 테스트: 중복 클릭, 창 닫기, 서버 종료, 자동 검사 겹침.
- 수정 시 위험: 취소를 “접속 실패”로 표시하지 않아야 한다.

### [UPNP-008] IPv4·IPv6 혼합 및 VPN 환경에서 네트워크 판정이 오해를 낳음

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 중간
- 관련 파일과 줄: `UpnpExternalAccess.cs:763-794`, `Launcher.decompiled.cs:3598-3654`
- 관련 함수: `IsCgnatPossible`, `GetNetworkDetails`
- 실제 동작: 라우터 IPv4와 외부 서비스 IPv6가 다르면 주소 계열을 구분하지 않고 CGNAT로 판정한다. 로컬 IPv4는 8.8.8.8 경로를 택해 VPN·가상 어댑터가 우선될 수 있다.
- 기대 동작: IPv4 WAN/공인 IPv4만 비교하고 실제 IGD가 연결된 인터페이스와 내부 클라이언트를 연결해야 한다.
- 발생 조건: IPv6 우선 회선, VPN, Wi-Fi/Ethernet 동시 사용.
- 사용자 영향: 잘못된 내부 IP로 매핑하거나 CGNAT 오탐.
- 보안 또는 데이터 영향: 다른 인터페이스로 포트 노출 시도 가능.
- 근본 원인: 외부 경로 선택과 IGD 장치 선택이 독립적이다.
- 재현 방법: 가짜 어댑터 집합과 IPv6 외부 응답을 주입한다.
- 권장 수정: 주소 계열별 상태와 IGD 인터페이스 연계를 추가한다.
- 필요한 회귀 테스트: VPN, dual-stack, APIPA, 다중 게이트웨이, 네트워크 전환.
- 수정 시 위험: Windows 라우팅 메트릭과 공유기 구현 차이를 장치 테스트해야 한다.

### [UPNP-009] COM 열거자 수명과 FinalRelease 정책이 명시적이지 않음

- 심각도: P2 Medium
- 분류: DESIGN_RISK
- 신뢰도: 중간
- 관련 파일과 줄: `UpnpExternalAccess.cs:551-598`, `UpnpExternalAccess.cs:903-927`
- 관련 함수: `FindUpnpMapping`, `ReleaseComObject`
- 실제 동작: `foreach`가 만든 COM 열거자 RCW는 직접 해제하지 않고 각 mapping과 collection에는 `FinalReleaseComObject`를 사용한다.
- 기대 동작: 생성한 모든 RCW의 소유권·해제 순서가 명시되고 공유 RCW를 강제 최종 해제하지 않아야 한다.
- 발생 조건: 많은 매핑 반복 조회, 공유기가 느린 열거자를 구현, RCW 별칭 발생.
- 사용자 영향: 장시간 실행 시 COM 자원 누수 또는 드문 `InvalidComObjectException` 가능.
- 보안 또는 데이터 영향: 없음.
- 근본 원인: late binding과 숨은 열거자.
- 재현 방법: COM shim으로 열거자 생성·해제 횟수와 RCW 별칭을 기록한다.
- 권장 수정: 명시적 `IEnumVARIANT` 래퍼 또는 COM 추상화로 수명 규칙을 고정한다.
- 필요한 회귀 테스트: 4096개 열거, 조기 return, 예외, 반복 검사.
- 수정 시 위험: 과도한 Release는 사용 중 RCW를 무효화할 수 있다.

### [UPNP-010] 공유기 설명 문자열 변형 시 고아 매핑이 생길 수 있음

- 심각도: P2 Medium
- 분류: NEEDS_DEVICE_TEST
- 신뢰도: 중간
- 관련 파일과 줄: `UpnpExternalAccess.cs:455-486`, `UpnpExternalAccess.cs:602-624`
- 관련 함수: `AcceptExistingUpnpMapping`, `DeleteCreatedUpnpMappings`
- 실제 동작: 설명이 12자 토큰까지 완전히 같아야 Add 응답 유실을 회수하고 종료 시 삭제한다.
- 기대 동작: 공유기 변형을 고려하되 사용자 매핑을 오인하지 않는 별도 소유권 증명이 필요하다.
- 발생 조건: 공유기가 설명을 자르거나 정규화·빈 문자열화.
- 사용자 영향: 생성은 됐지만 런처가 소유하지 않은 것으로 보고 매핑을 남긴다.
- 보안 또는 데이터 영향: 외부 포트 잔류.
- 근본 원인: 라우터가 보존한다고 보장할 수 없는 설명 필드에 소유권을 의존.
- 재현 방법: 설명을 16/20/32자로 절단하거나 공백을 바꾸는 가짜 장치 및 실제 장치 시험.
- 권장 수정: pending 기록, endpoint·시각·세션 토큰의 보수적 조합과 사용자 확인 기반 복구를 검토한다.
- 필요한 회귀 테스트: 설명 절단·대소문자·공백·빈 문자열.
- 수정 시 위험: 느슨한 일치는 UPNP-001을 재발시킨다.

### [UI-001] “UPnP 정리”가 UI를 막고 실패를 성공처럼 표시함

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `NetworkAndPlayerTools.cs:630-636`, `UpnpExternalAccess.cs:641-654`, `UpnpCore.cs:212-252`
- 관련 함수: 버튼 Click, `ClearAllMineHarborUpnpMappings`
- 실제 동작: UI 스레드에서 `Task.Wait()`/`Result`로 네트워크 요청을 기다린다. 예외는 0을 반환하고 UI는 “0개의 ... 지웠습니다”라고 정보 아이콘으로 표시한다.
- 기대 동작: 취소 가능한 비동기 진행 UI와 성공·부분 실패·검사 대상 없음 구분.
- 발생 조건: 느리거나 응답하지 않는 기록 URL, 네트워크 오류.
- 사용자 영향: 최대 HttpClient 기본 제한까지 창 정지, 실패 원인 은폐.
- 보안 또는 데이터 영향: 삭제 안전 판단을 어렵게 한다.
- 근본 원인: 동기 UI 핸들러와 손실되는 오류 모델.
- 재현 방법: 가짜 HTTP client가 완료되지 않거나 예외를 반환하도록 한다.
- 권장 수정: 버튼 경로를 비동기화하고 취소·세부 결과 모델을 노출한다.
- 필요한 회귀 테스트: timeout, 일부 성공, 취소, 창 닫기.
- 수정 시 위험: 중복 클릭과 Dispose 이후 콜백을 함께 차단해야 한다.

### [UI-002] 직접 콘솔에서 위험 명령 확인을 우회함

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `ModernLauncherGui.cs:871-915`, `ModernLauncherGui.cs:3720-3766`, `ManagedServerDashboard.cs:1262-1277`, `QuickCommandsAndBridge.cs:700-713`
- 관련 함수: `SendServerCommand`, `SendCommandFromBox`, `SendManagedCommand`, `RequiresQuickCommandConfirmation`
- 실제 동작: 빠른 명령 UI에는 확인 판정이 있지만 메인·관리 콘솔은 `stop`, `op`, `ban`, `save-off` 등을 직접 전송한다. 관리 콘솔은 줄바꿈 정규화도 공용 함수로 통일하지 않았다.
- 기대 동작: 모든 사용자 명령 전송 경로가 동일한 정규화·위험 판정·확인 정책을 거쳐야 한다.
- 발생 조건: 사용자가 직접 위험 명령을 입력하거나 자동완성 대신 콘솔 사용.
- 사용자 영향: 실수로 서버 종료·권한 변경·저장 비활성화.
- 보안 또는 데이터 영향: 로컬 운영 안전장치 우회.
- 근본 원인: 명령 정책이 UI 기능별로 분산됨.
- 재현 방법: 각 콘솔에 `stop`과 `save-off`를 입력하고 확인 대화상자 호출 여부를 모의 sender로 검사한다.
- 권장 수정: 단일 command dispatcher에서 정규화·위험 분류·확인·감사를 수행한다.
- 필요한 회귀 테스트: 모든 콘솔, 빠른 명령, 플레이어 관리, 브리지 후보.
- 수정 시 위험: 자동 종료 경로의 내부 `stop`에는 별도 신뢰 플래그가 필요하다.

### [DATA-001] 백업 ZIP의 총 해제량과 항목 수가 제한되지 않음

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `BackupAndProfileTools.cs:941-995`, `BackupAndProfileTools.cs:998-1040`, `BackupAndProfileTools.cs:1111-1131`
- 관련 함수: 백업 검증·추출
- 실제 동작: manifest 자체는 16MiB 제한이 있으나 파일 개수, 파일별 최대, 전체 uncompressed 크기 제한이 없다.
- 기대 동작: 입력 ZIP 크기, 항목 수, 파일별 및 총 해제 크기, 압축률을 제한하고 사전 디스크 공간을 확인해야 한다.
- 발생 조건: 악성 또는 손상된 외부 백업 ZIP.
- 사용자 영향: 디스크 고갈, 장시간 UI 작업, 복원 실패 흔적.
- 보안 또는 데이터 영향: 로컬 서비스 거부.
- 근본 원인: 해시 무결성과 자원 제한을 동일시했다.
- 재현 방법: 임시 폴더에서 작은 압축 크기·큰 해제 크기의 모의 ZIP을 사용한다.
- 권장 수정: Java 런타임 ZIP과 같은 누적 크기·항목 제한 및 스트리밍 카운터를 적용한다.
- 필요한 회귀 테스트: ZIP bomb, 수십만 항목, 디스크 부족, 취소.
- 수정 시 위험: 대형 월드 백업의 합리적 상한과 사용자 승인 경로가 필요하다.

### [SEC-004] 일반 빌드가 고정 비밀번호 개발 인증서를 영구 생성함

- 심각도: P2 Medium
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `build.ps1:70-72`, `scripts/sign-build.ps1:9-26`
- 관련 함수: 빌드 서명 단계
- 실제 동작: PFX가 없으면 `CurrentUser\My`에 인증서를 만들고 `mineharbor123`으로 내보낸 뒤 일반 빌드 산출물을 서명한다.
- 기대 동작: 빌드는 부작용 없이 서명 전 산출물을 만들고, 서명은 명시적 별도 단계에서 안전한 키 저장소를 사용해야 한다.
- 발생 조건: 새 개발 환경에서 `build.ps1` 실행.
- 사용자 영향: 빌드 실패 또는 예기치 않은 인증서·PFX 잔존.
- 보안 또는 데이터 영향: 고정 비밀번호 키 파일이 유출되면 해당 개발 인증서 가장 가능.
- 근본 원인: 개발 편의 서명과 재현 가능한 빌드를 결합.
- 재현 방법: 인증서·PFX가 없는 격리 사용자 프로필에서 빌드 부작용을 관찰한다.
- 권장 수정: 기본 빌드에서 서명을 제거하고 `-Sign` 명시 옵션과 OS 보호 키 저장소를 사용한다.
- 필요한 회귀 테스트: 무서명 빌드, 명시적 서명, 키 없음, 서명 실패.
- 수정 시 위험: 패키징 단계가 서명된 EXE를 덮어쓰지 않도록 순서를 정리해야 한다.

## 11. P3 및 접근성 발견 사항

### [DOC-001] README 표시 버전이 실제 버전과 불일치

- 심각도: P3 Low
- 분류: CONFIRMED
- 신뢰도: 높음
- 관련 파일과 줄: `README.md:29`, `README.md:190`, `version.json:2-3`
- 관련 함수: 해당 없음
- 실제 동작: 한국어·영어 README는 1.4.0/26.2.45.39, 실제 SSOT는 1.5.18/26.2.45.58이다.
- 기대 동작: 사용자 문서가 SSOT와 일치하거나 동적 배지로만 표시되어야 한다.
- 발생 조건: 저장소 README 열람.
- 사용자 영향: 지원·업데이트 판단 혼란.
- 보안 또는 데이터 영향: 없음.
- 근본 원인: 릴리스 시 문서 버전 동기화 검증 부재.
- 재현 방법: 두 파일 값 비교.
- 권장 수정: README 고정 버전을 제거하거나 CI 검증 추가.
- 필요한 회귀 테스트: 문서 버전 일치 검사.
- 수정 시 위험: 양언어 문구를 함께 갱신해야 한다.

### [MAINT-001] 일회성 수정 스크립트와 현재 API에 맞지 않는 테스트가 추적됨

- 심각도: P3 Low
- 분류: DESIGN_RISK
- 신뢰도: 높음
- 관련 파일과 줄: `fix_gui.py:1-39`, `fix_upnp.py:1-112`, `fix_publish.py:1-25`, `fix_title.py:1-15`, `test_upnp.cs:1-13`
- 관련 함수: 파일 직접 재작성, `Launcher.UpnpCore.DiscoverAndMapPortAsync`
- 실제 동작: 실행 시 제품 파일을 정규식으로 덮어쓰는 스크립트가 남아 있고 `test_upnp.cs`는 현재 존재하지 않는 API와 실제 포트 매핑 의도를 포함한다.
- 기대 동작: 유지보수 스크립트는 멱등성·검증·문서가 있거나 제거되고, 테스트는 모의 장치만 사용해야 한다.
- 발생 조건: 개발자가 파일명을 보고 실행.
- 사용자 영향: 소스 인코딩·내용 손상 또는 혼란.
- 보안 또는 데이터 영향: 잘못 복원될 경우 실제 UPnP 효과를 시도할 위험.
- 근본 원인: 임시 마이그레이션 도구 정리 누락.
- 재현 방법: 정적 API 참조와 파일 쓰기 대상을 검사한다. 실제 실행은 금지한다.
- 권장 수정: 필요한 이력은 문서화하고 실행 파일은 제거하거나 안전한 테스트 도구로 교체한다.
- 필요한 회귀 테스트: 저장소 테스트가 실제 네트워크 효과 API를 참조하지 않는지 정적 검사.
- 수정 시 위험: 과거 복구 절차 의존 여부를 먼저 확인한다.

### [UPNP-011] 소켓 MapPortsAsync가 실제 매핑 없이 성공을 반환함

- 심각도: P3 Low
- 분류: DESIGN_RISK
- 신뢰도: 높음
- 관련 파일과 줄: `UpnpCore.cs:168-191`
- 관련 함수: `SocketUpnpPortMappingService.MapPortsAsync`
- 실제 동작: 장치 URL만 발견하면 TODO 뒤 `Success=true`를 반환한다. 현재 제품 생성 경로에서는 호출되지 않는다.
- 기대 동작: 미완성 구현은 성공을 반환하지 않고 명시적으로 비활성화돼야 한다.
- 발생 조건: 향후 인터페이스 기반 생성 경로가 연결되는 경우.
- 사용자 영향: 매핑이 없는데 성공으로 표시될 수 있다.
- 보안 또는 데이터 영향: 잘못된 외부 노출 상태 판단.
- 근본 원인: 롤백 후 미완성 구현 잔존.
- 재현 방법: 가짜 SSDP 응답만 제공하고 Add SOAP 호출 없이 성공 여부 확인.
- 권장 수정: 구현 완료 전 `NotSupported` 실패로 바꾸거나 생산 빌드에서 제거한다.
- 필요한 회귀 테스트: 성공 시 Add 및 소유권 기록이 실제 호출됐음을 검증.
- 수정 시 위험: 수동 정리 경로가 같은 클래스를 사용하므로 역할을 분리해야 한다.

접근성 측면에서는 `AutoScaleMode.Dpi`, 최소 크기, 공통 `AccessibleName`, Enter/Esc, 키보드 탐색 및 대비 테스트가 다수 존재해 기본 구조는 양호하다. 다만 실제 125%, 150%, 200% DPI·스크린리더·다중 모니터 검증은 이번 정적 감사로 증명할 수 없다.

## 12. 오탐으로 확인된 항목

- `MessageBox.Show`: 제품 코드에는 직접 호출이 없고 공통 `ModernDialogs.cs`를 사용하며 테스트에서 재유입을 차단한다.
- 표준 사각형 `Button`: 직접 생성 패턴을 테스트에서 차단하고 공통 rounded button 계층을 사용한다.
- 명령 브리지 외부 노출: `TcpListener(IPAddress.Loopback, 0)`이며 원격 endpoint도 loopback인지 재검증한다.
- 브리지 토큰 약함: CSPRNG 32바이트, 매 실행 신규 생성, 고정 시간 비교, 메시지 길이·속도 제한이 확인됐다.
- Java ZIP Slip: 경로 루트, 재분석 지점, 항목 수·총 크기와 해시 검증이 구현돼 있다.
- 서버 휴지통 링크 추적 삭제: 재분석 지점을 따라가지 않고 링크 자체만 삭제한다.
- Modrinth 콘텐츠: 파일명·CDN·크기·SHA-512 우선 검증과 기존 파일 백업이 확인됐다.
- 최초 외부 검사 서비스 장애 시 UPnP 실행: 실제 코드는 `CheckCompleted=false`이면 UPnP를 실행하지 않아 안전하다.
- 다중 서버의 프로세스 내 전역 상태 공유: 관리 서버는 별도 런처 프로세스로 실행되므로 정적 필드는 프로세스 간 직접 공유되지 않는다.

## 13. 실제 장치 테스트가 필요한 항목

1. NATUPnP COM 생성·컬렉션·Add·Remove의 STA 동일 스레드 동작과 무응답 공유기.
2. 설명 문자열 절단·변형·빈 값, Add 성공 후 응답 유실, 컬렉션 반영 지연.
3. TCP 성공·UDP 실패/충돌, 서버 즉시 종료, 종료와 앱 닫기 동시 발생.
4. 공유기 외부 IPv4와 외부 검사 IPv4/IPv6 비교, CGNAT·이중 NAT·DS-Lite.
5. VPN·Hyper-V·WSL·Wi-Fi/Ethernet 동시 연결과 잘못된 내부 IPv4 선택.
6. Windows 방화벽의 Allow/Block, 프로필, 프로그램 경로, 서비스, 주소 범위 우선순위.
7. 두 MineHarbor 프로세스가 같은 공유기와 같거나 다른 포트를 동시에 조작하는 경우.
8. Windows 10/11과 여러 공유기 제조사의 COM 열거자·RCW 수명.
9. 125%, 150%, 200% DPI, 다중 모니터, 고대비, Narrator 및 키보드 전용 흐름.

장치 시험 전에는 실제 매핑을 만들지 않는 COM shim·가짜 포트 매핑 컬렉션으로 상태 머신을 먼저 통과시켜야 한다. 장치 시험은 전용 공유기·격리 회선·복구 가능한 설정에서 사용자가 명시적으로 승인한 경우에만 수행한다.

## 14. 권장 수정 순서

1. “UPnP 정리” 버튼을 임시 비활성화하고 UPNP-001 삭제 안전성을 먼저 해결한다.
2. COM 생성·소유권 저장·복구·삭제를 하나의 구현 독립 상태 머신으로 통합한다.
3. COM 호출 제한 시간·취소·프로토콜별 부분 성공 결과와 외부 검사 3상태 모델을 도입한다.
4. TEST-001을 강타입 가짜 COM/검사 서비스 테스트로 교체하고 안전 불변 조건을 릴리스 게이트로 만든다.
5. Forge와 로컬 Inno Setup 공급망 검증, 리디렉션·응답 크기 정책을 통합한다.
6. 백업 archive와 manifest 집합 동일성 및 자원 제한을 적용한다.
7. UPnP 정리·재검사 UI를 취소 가능한 비동기로 바꾸고 방화벽 상태를 보수적으로 표현한다.
8. 모든 명령 전송 경로를 단일 위험 확인 정책으로 통합한다.
9. 문서 버전과 임시 유지보수 파일을 정리한다.

## 15. 권장 테스트 추가 목록

- COM 접근 추상화와 가짜 `NATUPnP`, collection, mapping, enumerator.
- 가짜 외부 검사 서비스, 가짜 시간·재시도·취소 정책.
- TCP 성공 후 UDP 실패·충돌·취소 상태 머신.
- 기존 사용자 매핑 보호와 삭제 전 소유권 변경.
- Add 성공 후 응답 예외와 설명 문자열 변형.
- COM 무응답과 45초 종료 경계.
- 프로필별 자동·수동 검사 동시성 및 오래된 UI 결과 억제.
- VPN·dual-stack·CGNAT 주소 선택.
- 방화벽 프로그램·프로필·Block 우선순위.
- Forge 교차 호스트 redirect·해시 불일치·크기 초과.
- 백업 미등재 파일·ZIP bomb·중복/대소문자 경로.
- 메인/관리/빠른 명령의 동일 위험 확인.
- CI에서 실제 테스트 훅 부재와 미사용 fake를 실패시키는 정적 검사.

## 16. 잔여 위험

- 실제 공유기 없이 COM 구현 차이와 설명 보존 여부를 증명할 수 없다.
- portchecker.io가 반환하는 의미와 IPv4/IPv6 동작은 외부 서비스 계약 변화에 영향을 받는다.
- 기존 `HttpListener` 의존 테스트는 플랫폼 독립 안전 불변 조건 테스트로 교체되어 전체 테스트가 통과했다.
- 일반 빌드와 명시적 인증서 서명 경로를 분리했으며, 실제 배포 인증서의 SmartScreen 평판은 별도 운영 과제다.
- 실제 DPI·스크린리더·다중 모니터 UX는 장치 행렬 시험이 필요하다.
- 최초 감사 작성 시점에는 재현 계획만 수립하고 제품 코드·버전·네트워크 설정을 수정하지 않았으며, 아래 17절이 후속 수정 결과다.

## 17. 후속 수정 결과 (2026-07-18)

이 감사 이후 `1.5.19` 수정 작업에서 P0~P3 항목을 코드·테스트·문서에 반영했다. 특히 삭제 전 현재 매핑 소유권 재검증, 실행별 COM 매핑 기록과 비정상 종료 복구, TCP/UDP 부분 성공 표시, 외부 검사 3상태와 중복 실행 차단, 보수적 방화벽·CGNAT 판정, 비동기 정리 UI를 구현했다.

Forge Installer와 로컬 Inno Setup은 SHA-256을 확인하고 다운로드 리디렉션과 크기를 제한한다. 백업은 archive와 manifest의 집합이 정확히 같아야 하며 항목 수와 총 해제 크기를 제한한다. 메인·관리 콘솔은 공통 명령 정규화와 위험 확인을 사용하고, 일반 빌드는 더 이상 고정 비밀번호 개발 인증서나 사용자 인증서 저장소를 만들지 않는다.

회귀 검증은 `PASSED=21`, `BRIDGE_PROTOCOL_PASSED=8`, Portable 버전·smoke 및 modern dialog scan 통과로 완료했다. 실제 공유기 펌웨어별 설명 보존, COM 무응답, dual-stack·VPN·방화벽 조합과 DPI·스크린리더는 13절의 격리 장치 행렬로 최종 확인해야 한다. COM 호출 자체는 운영체제 API가 취소를 제공하지 않아 프로세스 내부에서 안전하게 강제 종료할 수 없으므로, 호출자는 시간 제한 후 UI를 반환하고 실행별 기록을 다음 실행에서 재검증·복구한다.
