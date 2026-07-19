# AI Agent Synchronization State

## Codex UPnP Lifecycle/Fallback Remediation - 2026-07-20

- **Current Version**: 1.5.21 (build 26.2.45.61)
- **Branch**: `codex/fix-upnp-lifecycle`
- **Status**: UPnP 수명 주기·대체 경로 보강과 정식 GitHub 자동 업데이트 릴리스 게시
- 비활성 상태였던 직접 SSDP/SOAP 매핑을 기본 경로로 구현하고, 실패할 때 Windows NATUPnP COM을 보조 경로로 사용합니다. 서비스 검색은 인터페이스별 내부 IP를 보존하며 응답 수·XML 크기·HTTP 시간·안전한 라우터 주소를 제한합니다.
- 기본 외부 포트가 다른 매핑과 충돌하면 최대 8개의 대체 외부 포트를 순서대로 시도하고, 실제 선택된 외부 포트를 상태·접속 주소·재검사·정리에 일관되게 사용합니다. 사용자가 만든 기존 매핑은 소유하지 않으며 삭제하지 않습니다.
- 서버 중단 토큰과 직접 SOAP 호출을 연결하고, 이전 실행의 지연 정리가 새 실행의 매핑을 삭제하지 않도록 실행 세대 검사를 추가했습니다. 종료되지 않은 COM 정리 작업이 있으면 새 매핑 시작을 차단하여 반복 온·오프 경합을 방지합니다.
- 직접 경로는 정확한 라우터 상태 조회 후에만 소유 매핑을 삭제하며, 추가 응답이 유실된 경우에도 라우터의 실제 상태를 재조회해 성공을 복구합니다. 취소 중 남은 소유 기록은 다음 실행의 안전한 복구 대상으로 표시합니다.
- 회귀 검증에는 직접 SOAP TCP/UDP 8회 반복 생성·삭제, COM 12회 반복 생성·삭제, 기본 포트 충돌과 대체 포트, 응답 유실, 요청 중 취소, 사용자 매핑 보존, 이전 실행 지연 정리 경합이 포함됩니다. 테스트는 루프백 가짜 공유기만 사용하도록 고정했습니다.
- 검증 결과: `Prepare-BuildResources.ps1` 성공, `build.ps1` 성공, `test.ps1 -LauncherPath artifacts\MineHarbor.exe`에서 `PASSED=21`, `PORTABLE_VERSION_OK`, `PORTABLE_SMOKE_OK`, `BRIDGE_PROTOCOL_PASSED=8`, `MODERN_DIALOG_SCAN_OK`, `SECURITY_REGRESSION_SCAN_OK` 통과.
- 런처가 조회하는 최신 릴리스 API와 `update.json`의 실행 파일·브리지 다운로드 URL을 정식 `Mangom72/MineHarbor` 저장소로 통일하고, 릴리스 검증에서 정확한 URL·크기·SHA-256을 확인합니다.
- 테스트 개발 중 실제 공유기에 정확히 한 개의 격리된 `25602/TCP -> 127.0.0.1:25565` 시험 매핑이 생성되었습니다. 즉시 설명·프로토콜·외부/내부 포트를 재검증한 뒤 해당 항목만 삭제했으며 부재를 다시 확인했습니다. 이후 시험은 실제 SSDP 탐색을 호출하지 않습니다.
- 남은 검증은 제조사·펌웨어별 공유기, 이중 NAT/CGNAT, NAT-PMP/PCP 전용 장치에 대한 격리 장치 행렬입니다. NAT-PMP/PCP는 현재 구현 범위에 포함하지 않았습니다.

## Codex Security Remediation - 2026-07-18

- **Current Version**: 1.5.20 (build 26.2.45.60)
- **Branch**: `codex/release-v1.5.20`
- **Status**: 감사 수정본을 정식 패치 릴리스로 게시하기 위한 버전·변경 기록 및 태그 준비
- UPnP 매핑을 실행별로 기록하고 현재 내부 IP·포트·프로토콜·설명이 정확히 일치할 때만 삭제합니다. 살아 있는 다른 MineHarbor 프로세스의 기록은 보존합니다.
- TCP/UDP 부분 성공, 외부 검사 실패, 중복 재검사, CGNAT dual-stack 비교, 가상 어댑터와 방화벽 규칙 판정을 보수적으로 처리합니다.
- Forge/Inno Setup 다운로드, 백업 복원, 직접 콘솔 위험 명령, 로컬 코드 서명 경로를 강화했습니다.
- 기준 검증: `build.ps1 -SkipDependencyDownload` 성공, `test.ps1 -LauncherPath artifacts\MineHarbor.exe`에서 `PASSED=21`, `BRIDGE_PROTOCOL_PASSED=8`, 버전·smoke·modern-dialog scan 통과.
- 실제 공유기 COM 무응답을 프로세스 내부에서 강제로 중단하는 것은 안전하지 않으므로 호출자는 제한 시간 후 복구 기록을 남기며, 제조사별 장치 시험은 별도 격리 환경에서 수행해야 합니다.

## Current Session Status
- **Agent Name**: Antigravity
- **Last Updated**: 2026-07-16T07:18:00Z
- **Current Version**: 1.5.17 (build 26.2.45.57)
- **Status**: Stable / Released

## Recent Accomplishments
1. **DWM Title Bar Cohesion (Windows 11)**:
   - Integrated TitleBarDwm using dwmapi.dll (DWMWA_CAPTION_COLOR, DWMWA_TEXT_COLOR, DWMWA_BORDER_COLOR) to seamlessly blend the native OS title bar with the application's internal dark/light theme on Windows 11 (Build >= 22000).
   - Preserved FormBorderStyle.Sizable, maintaining 100% of native Windows features like Snap Layouts, multi-monitor DPI scaling, and drop shadows.
   - Handled graceful fallback to default OS title bar for Windows 10 without throwing exceptions.
2. **Handle Recreation Hotfix (v1.5.17)**:
   - Fixed an issue where the C# Form.OnHandleCreated method could not be overridden properly due to internal visibility modifier constraints.
   - Switched to subscribing to the HandleCreated event inside the form constructors (LauncherForm and ServerSetupForm).
   - Ensures DWM color states are preserved even when the window handle is forcefully recreated by the WinForms engine (e.g. ShowInTaskbar updates).
3. **Release v1.5.17**:
   - Bumped version to 1.5.17.
   - Updated CHANGELOG.md with multi-language hotfix notes.
   - Successfully ran Publish-LocalRelease.ps1 and pushed the new artifacts to GitHub Releases.

## Active Issues / Next Steps
- **Monitoring**: Everything is currently stable. Monitor for any reports of unhandled exceptions from dwmapi.dll calls on unusual Windows versions.

## Workspace Context
- **Encoding Rule**: All PowerShell .ps1, C# .cs, and .md files must be saved with **UTF-8 with BOM** to avoid build failures with the local csc.exe and signtool.
- **Certificates**: Local signing throws warnings in PS but still signs successfully.
