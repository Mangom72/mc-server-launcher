# AI Agent Synchronization State

## Codex Content, Automation, and Dashboard - 2026-07-20

- **Current Version**: 1.6.0 (build 26.2.45.64)
- **Branch**: `codex/content-scheduler-dashboard-v1.6.0`
- **Status**: 콘텐츠 관리·서버 자동화·상태 대시보드 구현, 로컬 릴리스 검증 및 PR #8 Windows CI 완료
- 서버별 `.mineharbor/content-manifest.json`을 도입해 MineHarbor 관리 파일과 수동 설치 파일을 구분합니다. 플러그인·모드·데이터팩 검색, 호환성 검사, 필수 의존성 처리, 해시 검증, 설치·업데이트·일괄 업데이트·활성화 전환·복구 가능한 제거를 지원합니다.
- 데이터팩은 Vanilla, Paper, Purpur와 직접 JAR 프로필에서 월드별 `datapacks` 폴더를 대상으로 관리합니다. ZIP 루트의 `pack.mcmeta`, `pack_format`, 경로 이탈·중복, 항목 수와 해제 크기를 설치 전에 검사합니다.
- 서버별 `.mineharbor/automation.json`에 시작 전·종료 후 백업, 정기 백업, 예약 시작·종료·재시작·명령, 플레이어 사전 공지, 최근 결과와 다음 실행, 개수·기간·용량 보존 정책을 저장합니다. 프로세스 ID와 시작 시각을 포함한 디스크 실행 임대로 중복 실행과 비정상 종료 후 잠금 잔존을 처리합니다.
- 통합 서버 대시보드는 상태·가동 시간·CPU·메모리·Java·플레이어·서버/월드/백업 크기·최근 경고/오류·외부 접속·다음 작업을 보여 줍니다. Paper/Purpur 브리지는 TPS/MSPT를 주기적으로 전송하고, 얻을 수 없는 값은 추정하지 않고 지원되지 않음으로 표시합니다.
- 콘텐츠·일정·백업·대시보드 화면을 서버 관리에서 분리하고 다크/라이트 테마, DPI 재배치, 키보드 접근, 접근성 이름, 진행률과 취소를 적용했습니다. 폼 종료 후 비동기 UI 콜백은 취소 토큰과 폐기 상태 검사로 차단합니다.
- SDK 스타일 `net48` 프로젝트를 추가해 기존 Portable 빌드와 병행 검증하도록 했습니다. 기본 런타임의 .NET 10 전환은 프레임워크 전용 JSON API, WinForms/COM, 단일 EXE 업데이트 및 설치 호환성 검증이 끝날 때까지 보류합니다.
- PR 및 `main` push용 일반 CI를 릴리스 워크플로와 분리했습니다. 버전·문서·소스 목록 동기화, SDK/기존 빌드, 실행·실패 경로·UI·브리지 테스트, Portable 메타데이터를 검사하며 모든 외부 Action을 전체 커밋 SHA로 고정했습니다.
- 최종 로컬 검증: 릴리스 산출물 7종 구조·SHA-256 통과, `VERSION_CONSISTENCY_OK`, `PASSED=24`, `PORTABLE_VERSION_OK`, `PORTABLE_SMOKE_OK`, `BRIDGE_PROTOCOL_PASSED=10`, `MODERN_DIALOG_SCAN_OK`, `SECURITY_REGRESSION_SCAN_OK`. UPnP 검증은 루프백 가짜 장치와 가짜 COM만 사용했고 실제 공유기 설정은 변경하지 않았습니다.
- GitHub PR [#8](https://github.com/Mangom72/MineHarbor/pull/8), Actions 실행 `29729415761`에서 빈 캐시 의존성 검증, .NET 10 SDK의 `net48` 빌드, 기존 Portable 빌드·테스트와 검증 산출물 업로드가 모두 통과했습니다. Paper Maven의 UI 리디렉션 문제는 공식 Artifactory 원본 URL과 기존 SHA-256을 함께 고정해 수정했습니다.
- 남은 검증은 실제 DPI/다중 모니터·제조사별 서버 설치 환경, Fabric/Forge용 TPS 브리지, Windows 코드 서명입니다.

## Codex Full Product Audit and Release - 2026-07-20

- **Current Version**: 1.5.23 (build 26.2.45.63)
- **Branch**: `codex/v1.5.23-release-state`
- **Status**: UPnP·자동 업데이트·UI/UX·접근성·네트워크·공급망 전제품 감사 수정 및 `v1.5.23` 정식 릴리스 게시 완료
- 이전 릴리스의 직접 SSDP/SOAP 기본 경로, NATUPnP COM 백업, 8개 대체 외부 포트, 세대·취소·정확한 소유권 정리를 재검증했습니다. 테스트는 루프백 가짜 공유기와 가짜 COM만 사용하며 이번 작업에서 실제 공유기 매핑을 만들지 않았습니다.
- GitHub 릴리스 별칭에서 정식 저장소와 CDN으로 이어지는 자동 업데이트·명령 브리지 리디렉션을 검증된 수동 경로로 수정했습니다. 메타데이터·파일 크기, 저장소·버전·파일명, 최종 SHA-256을 확인하며 공개 `v1.5.23` 브리지 자산 다운로드도 통과했습니다.
- Modrinth API/CDN과 아이콘 변환 응답의 리디렉션·크기·디코딩 픽셀 수를 제한하고, 아이콘 프록시 개인정보 안내를 한국어·영어로 갱신했습니다.
- 영어 업데이트 노트, 보조 창 DPI/버튼 배치, 모델리스 타이머 해제, 스크린 리더 이름과 빠른 명령 영문 문구를 수정했습니다. 격리 프로필로 주요 Windows 화면, 한국어·영어와 다크·라이트 테마를 실제 확인했습니다.
- GitHub Actions를 전체 SHA로 고정하고 체크아웃 자격 증명 저장을 끄며 서명 비밀의 범위와 임시 PFX 정리를 강화했습니다. 버전 도구의 경로·모드·인코딩 검증과 CI 정적 회귀 조건을 추가했습니다.
- 깨진 과거 `CHANGELOG.md` 인코딩을 읽을 수 있는 이중 언어 요약으로 복구하고 오래된 일회성 계획·우회 스크립트를 제거했습니다.
- 최종 검증: `PASSED=22`, `PORTABLE_VERSION_OK`, `PORTABLE_SMOKE_OK`, `BRIDGE_PROTOCOL_PASSED=8`, `MODERN_DIALOG_SCAN_OK`, `SECURITY_REGRESSION_SCAN_OK`, 릴리스 자산 7종 구조·해시 검증을 통과했습니다. GitHub Actions 실행 `29700540478`의 빌드·테스트·설치 파일·게시 단계가 모두 성공했으며, 코드 서명 비밀이 없어 서명 단계만 건너뛰었습니다.
- 공개 `v1.5.23` 자산을 다시 내려받아 `SHA256SUMS.txt`와 `update.json`의 실행 파일·브리지 크기 및 SHA-256을 대조했습니다. 공개 `v1.5.22`와 `v1.5.20` 런처의 실제 자동 업데이트 루틴이 `v1.5.23`을 탐지하고 새 실행 파일을 다운로드·검증하는 호환성 시험도 통과했습니다.

## Codex UPnP Lifecycle/Fallback Remediation - 2026-07-20

- **Current Version**: 1.5.22 (build 26.2.45.62)
- **Branch**: `codex/fix-auto-update-v1.5.22`
- **Status**: UPnP 수명 주기·대체 경로 보강과 정식 GitHub 자동 업데이트 릴리스 게시
- 비활성 상태였던 직접 SSDP/SOAP 매핑을 기본 경로로 구현하고, 실패할 때 Windows NATUPnP COM을 보조 경로로 사용합니다. 서비스 검색은 인터페이스별 내부 IP를 보존하며 응답 수·XML 크기·HTTP 시간·안전한 라우터 주소를 제한합니다.
- 기본 외부 포트가 다른 매핑과 충돌하면 최대 8개의 대체 외부 포트를 순서대로 시도하고, 실제 선택된 외부 포트를 상태·접속 주소·재검사·정리에 일관되게 사용합니다. 사용자가 만든 기존 매핑은 소유하지 않으며 삭제하지 않습니다.
- 서버 중단 토큰과 직접 SOAP 호출을 연결하고, 이전 실행의 지연 정리가 새 실행의 매핑을 삭제하지 않도록 실행 세대 검사를 추가했습니다. 종료되지 않은 COM 정리 작업이 있으면 새 매핑 시작을 차단하여 반복 온·오프 경합을 방지합니다.
- 직접 경로는 정확한 라우터 상태 조회 후에만 소유 매핑을 삭제하며, 추가 응답이 유실된 경우에도 라우터의 실제 상태를 재조회해 성공을 복구합니다. 취소 중 남은 소유 기록은 다음 실행의 안전한 복구 대상으로 표시합니다.
- 회귀 검증에는 직접 SOAP TCP/UDP 8회 반복 생성·삭제, COM 12회 반복 생성·삭제, 기본 포트 충돌과 대체 포트, 응답 유실, 요청 중 취소, 사용자 매핑 보존, 이전 실행 지연 정리 경합이 포함됩니다. 테스트는 루프백 가짜 공유기만 사용하도록 고정했습니다.
- 검증 결과: `Prepare-BuildResources.ps1` 성공, `build.ps1` 성공, `test.ps1 -LauncherPath artifacts\MineHarbor.exe`에서 `PASSED=21`, `PORTABLE_VERSION_OK`, `PORTABLE_SMOKE_OK`, `BRIDGE_PROTOCOL_PASSED=8`, `MODERN_DIALOG_SCAN_OK`, `SECURITY_REGRESSION_SCAN_OK` 통과.
- 런처가 조회하는 최신 릴리스 API는 정식 `Mangom72/MineHarbor` 저장소를 사용합니다. `update.json`의 실행 파일·브리지 URL은 `v1.5.20` 이하의 자동 업데이트를 위해 GitHub가 유지하는 이전 저장소 별칭을 사용하며, 새 런처는 정식·별칭 경로를 모두 검증합니다.
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
