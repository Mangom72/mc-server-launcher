# Changelog

제품 버전은 [Semantic Versioning](https://semver.org/)을 사용하며, `26.2.45.xx` 값은 별도의 내부 빌드 번호입니다.

Product versions follow [Semantic Versioning](https://semver.org/), while `26.2.45.xx` is a separate internal build number.

## [1.7.0] - 2026-07-20

### Korean

- **현대형 테마 컨트롤**: 다크·라이트 팔레트를 공유하는 둥근 체크박스, 드롭다운, 탭, 목록 헤더·행과 상태 표 구분선을 추가했습니다. 고정 격자와 항상 표시되던 대시보드 스크롤을 제거하고 키보드 포커스·스크린 리더 정보를 유지했습니다.
- **플레이어·명령 자동완성**: 플레이어 관리 화면에서 연결된 플레이어 이름을 제안하고, 멀티 서버 콘솔에서 기본 명령과 온라인 플레이어 인수를 위아래 방향키 및 Tab/Enter로 완성할 수 있습니다.
- **외부 포트 판정 수정**: 일반 TCP 포트 검사 결과를 Minecraft 서버 확인으로 오인하지 않도록 `서버 일치 미확인` 상태를 도입했습니다. MineHarbor가 생성한 UPnP 매핑의 사후 검사만 `확인됨`으로 표시하고 외부 검사 요청의 캐시를 차단합니다.
- **휴지통 UX 개선**: 서버 이름 입력은 휴지통으로 보내는 단계에만 유지하고 연한 서버명 예시를 표시합니다. 휴지통의 영구 삭제는 이름 재입력 대신 3초 동안 잠긴 테마 확인 창을 사용합니다.
- **창 닫기 안전성**: 모델리스 도구 창의 제목 표시줄 X를 눌러 닫을 때 같은 위치의 메인 창 버튼이 함께 눌리지 않도록 짧은 마우스 메시지 보호 구간을 추가했습니다.
- **검증**: 자동완성, 보수적 외부 상태, 3초 확인, 현대형 상태 표와 클릭 관통 보호 회귀를 포함한 25개 런처 테스트 그룹 및 10개 브리지 프로토콜 테스트를 통과했습니다. 네트워크 테스트는 실제 공유기 대신 루프백 가짜 UPnP 장치와 가짜 COM을 사용합니다.

### English

- **Modern themed controls**: Added rounded checkboxes, dropdowns, tabs, list headers/rows, and metric separators sharing the dark/light palette. Removed the fixed dashboard grid and always-enabled scrolling while preserving keyboard focus and screen-reader metadata.
- **Player and command completion**: Player management now suggests connected player names, while managed-server consoles complete common commands and online-player arguments with Up/Down and Tab/Enter.
- **Correct external-port classification**: Generic TCP results are now reported as `server identity unverified` instead of being treated as proof of Minecraft reachability. Only post-checks for UPnP mappings created by MineHarbor become verified, and external-check requests bypass caches.
- **Safer Trash UX**: Exact-name input remains only when moving a server to Trash and shows a light server-name cue. Permanent deletion inside Trash now uses a themed confirmation locked for three seconds instead of asking for the name again.
- **Close-button safety**: Added a short mouse-message guard so closing a modeless tool with its title-bar X cannot also click a main-window button underneath.
- **Verification**: Passed 25 launcher test groups and 10 bridge protocol tests, including completion, conservative external status, timed confirmation, modern metric layout, and click-through guards. Network tests use loopback fake UPnP devices and fake COM instead of a real router.

## [1.6.0] - 2026-07-20

### Korean

- **통합 콘텐츠 관리**: `.mineharbor/content-manifest.json`을 도입해 설치된 플러그인·모드·데이터팩을 수동 파일과 구분하고, 호환 버전·로더 및 필수 의존성을 검사한 검색·설치·개별/일괄 업데이트·비활성화·복구 가능한 제거를 추가했습니다.
- **데이터팩 검증**: Vanilla, Paper, Purpur와 직접 JAR 프로필의 월드를 찾아 `world/datapacks`에 설치하며, 루트 `pack.mcmeta`, 안전한 ZIP 경로, 중복 항목, 압축 파일 수와 해제 크기를 검증합니다.
- **서버 자동화**: `.mineharbor/automation.json`에 서버별 정기 백업, 시작 전·종료 후 백업, 예약 시작·종료·재시작·명령, 플레이어 사전 공지, 다음/최근 실행 결과와 개수·기간·총용량 보존 정책을 저장합니다. MineHarbor 관리 창이 실행 중일 때 일정을 평가하며, 원자적 실행 임대로 중복 실행과 만료된 실행 상태를 처리합니다.
- **운영 대시보드**: 서버 상태·가동 시간, Java CPU·메모리·버전, 플레이어, 서버·월드·백업 용량, 최근 경고·오류, 외부 접속 결과와 다음 예약을 표시합니다. Paper/Purpur 브리지가 실제 제공한 경우에만 TPS/MSPT를 표시합니다.
- **비동기·UI 구조 개선**: 콘텐츠·자동화·상태 수집을 별도 partial 서비스/화면으로 분리하고, 장시간 작업에 `Task`, `async/await`, `CancellationToken`, 진행률과 닫힌 UI 콜백 차단을 적용했습니다.
- **빌드와 검증 강화**: PR/main CI와 `net48` SDK 스타일 병행 프로젝트, 버전·문서 일치 검사, Portable EXE·브리지 검증을 추가하고 손상 manifest, 해시 불일치, 의존성 순환, 잘못된 데이터팩, 중복 일정, 백업 실패, 브리지 연결 해제와 UI 종료 회귀를 포함한 24개 테스트 그룹을 통과했습니다.

### English

- **Unified content management**: Added `.mineharbor/content-manifest.json`, managed/manual distinction, compatibility and required-dependency checks, search/install, individual or batch updates, enable/disable, and recoverable removal for plugins, mods, and data packs.
- **Data-pack validation**: Discovers worlds for Vanilla, Paper, Purpur, and custom-JAR profiles, installs into `world/datapacks`, and validates root `pack.mcmeta`, safe ZIP paths, duplicates, entry counts, and expanded size.
- **Server automation**: Stores per-server recurring and start/stop-hook backups, scheduled start/stop/restart/commands, player warnings, next/latest results, and count/day/size retention in `.mineharbor/automation.json`. Schedules are evaluated while a MineHarbor management window is running; atomic execution leases prevent duplicate jobs and recover expired runs.
- **Operations dashboard**: Shows status, uptime, Java CPU/memory/version, players, server/world/backup size, recent warnings/errors, external-access results, and the next job. TPS/MSPT appear only when actually supplied by the Paper/Purpur bridge.
- **Async and UI structure**: Split content, automation, and status collection into dedicated partial services/forms and applied `Task`, `async/await`, cancellation, progress, and closed-UI callback guards to long operations.
- **Build and verification**: Added PR/main CI, a parallel SDK-style `net48` project, version/document consistency checks, Portable and bridge validation, and 24 test groups covering corrupt manifests, hash mismatch, dependency cycles, invalid data packs, duplicate schedules, backup failures, bridge disconnects, and closed UIs.

## [1.5.23] - 2026-07-20

### Korean

- **자동 업데이트와 명령 브리지 복구**: GitHub Release 별칭에서 CDN까지 이어지는 리디렉션을 허용 호스트·저장소·버전·파일명으로 검증하고, 메타데이터와 바이너리 크기를 제한해 실제 공개 브리지 설치와 이전 런처 자동 업데이트가 모두 동작하도록 수정했습니다.
- **네트워크·이미지 보안 강화**: Modrinth API와 CDN 응답의 리디렉션·크기를 제한하고, 아이콘 변환 URL을 안전하게 인코딩하며 디코딩된 이미지의 가로·세로와 전체 픽셀 수를 제한했습니다.
- **UI·UX·접근성 개선**: 영어 업데이트 화면에 영어 릴리스 노트를 표시하고, 보조 창 DPI 배율·버튼 줄바꿈·타이머 해제를 보완했으며, 입력·목록·콘솔의 스크린 리더 이름과 영어 빠른 명령 문구를 개선했습니다.
- **빌드 공급망 강화**: GitHub Actions를 전체 커밋 SHA로 고정하고 체크아웃 자격 증명 유지를 끄며, 코드 서명 비밀을 필요한 단계로만 제한하고 임시 PFX를 항상 정리하도록 변경했습니다.
- **검증과 유지보수**: UPnP 반복 시작·중단, 매핑 소유권·대체 포트·지연 정리 회귀를 포함한 22개 런처 테스트와 8개 브리지 프로토콜 테스트를 통과했으며, 깨진 변경 이력 인코딩과 오래된 일회성 스크립트를 정리했습니다.

### English

- **Restored automatic updates and bridge downloads**: Validated redirects from GitHub Release aliases through the CDN by host, repository, version, and filename, bounded metadata and binaries, and restored both public bridge installation and updates from older launchers.
- **Hardened network and image handling**: Bounded redirects and response sizes for the Modrinth API and CDN, safely encoded image-proxy URLs, and limited decoded image dimensions and total pixel count.
- **Improved UI, UX, and accessibility**: Displayed English release notes in the English update dialog, improved secondary-window DPI scaling and button wrapping, disposed timers, and added screen-reader names for inputs, lists, and consoles.
- **Hardened the build supply chain**: Pinned GitHub Actions to full commit SHAs, disabled persisted checkout credentials, scoped signing secrets to required steps, and always removed temporary PFX files.
- **Expanded verification and maintenance**: Passed 22 launcher test groups—including repeated UPnP start/stop, ownership, alternate-port, and delayed-cleanup regressions—plus 8 bridge protocol tests, and repaired corrupted changelog history and obsolete one-off scripts.

## [1.5.22] - 2026-07-20

### Korean

- 기존 저장소 별칭 URL을 메타데이터에 유지해 `v1.5.20` 이하 런처의 자동 업데이트 호환을 복구했습니다.
- 새 런처는 정식 저장소와 이전 별칭의 엄격한 GitHub Release 경로만 허용합니다.

### English

- Restored automatic updates for launchers through `v1.5.20` by retaining the legacy repository-alias URL in release metadata.
- New launchers accept only strict GitHub Release paths from the canonical repository or its legacy alias.

## [1.5.21] - 2026-07-20

### Korean

- 직접 SSDP/SOAP UPnP를 기본 경로로 완성하고 Windows COM 백업과 최대 8개 대체 외부 포트를 추가했습니다.
- 실행 세대·취소·소유권 검증으로 반복 시작·중단과 지연 정리 경합을 안정화했습니다.

### English

- Completed direct SSDP/SOAP UPnP as the primary path, retained Windows COM fallback, and added up to eight alternate external ports.
- Stabilized repeated start/stop and delayed cleanup races with generations, cancellation, and exact ownership checks.

## [1.5.20] - 2026-07-18

### Korean

- UPnP 실행별 소유권, 외부 접속 진단, Forge/Inno Setup 검증, 백업 압축 해제 제한과 위험 명령 확인을 강화했습니다.

### English

- Hardened per-run UPnP ownership, external-access diagnostics, Forge/Inno Setup verification, backup extraction limits, and dangerous-command confirmation.

## 이전 릴리스 요약 / Earlier release summary

| 버전 | 날짜 | 한국어 | English |
|---|---|---|---|
| 1.5.19 | 2026-07-18 | UPnP 매핑 소유권과 외부 접속 복구, 공급망·백업 보안을 강화했습니다. | Hardened UPnP ownership and external-access recovery, plus supply-chain and backup security. |
| 1.5.18 | 2026-07-16 | 불완전한 소켓 UPnP를 안정적인 COM 경로로 되돌리고 타이틀 바와 버튼 정렬을 수정했습니다. | Reverted incomplete socket UPnP to the stable COM path and fixed title-bar and button alignment. |
| 1.5.17 | 2026-07-16 | 시작 중 HWND 재생성 뒤에도 DWM 타이틀 바 테마가 유지되도록 수정했습니다. | Preserved DWM title-bar theming after startup HWND recreation. |
| 1.5.16 | 2026-07-16 | Windows 11 기본 타이틀 바를 앱 테마와 동기화하면서 스냅·DPI 동작을 유지했습니다. | Synchronized the Windows 11 native title bar with the app theme while preserving snap and DPI behavior. |
| 1.5.15 | 2026-07-15 | 소켓 기반 UPnP, 고아 매핑 추적, 안전한 XML 파싱과 비동기 탐색을 도입했습니다. | Introduced socket-based UPnP, orphan tracking, safe XML parsing, and asynchronous discovery. |
| 1.5.14 | 2026-07-15 | 콘텐츠의 WebP·SVG 아이콘을 호환 PNG로 변환해 표시했습니다. | Added compatible PNG conversion for WebP and SVG content icons. |
| 1.5.13 | 2026-07-15 | 다운로드·TLS·CRLF 보안, 공인 IP 백업 서비스, 업데이트 복구 UX를 강화했습니다. | Hardened downloads, TLS, and CRLF handling, added public-IP fallbacks, and improved update recovery UX. |
| 1.5.12 | 2026-07-15 | 업데이트 무결성, Job Object, 강제 종료 예외와 UI 비동기 처리를 개선했습니다. | Improved update integrity, Job Objects, force-stop exceptions, and asynchronous UI handling. |
| 1.5.11 | 2026-07-15 | 프로세스 수명 주기와 업데이트 안정성을 정비하고 환경 변수 삽입과 강제 종료 교착을 수정했습니다. | Refactored process lifecycle and update stability, and fixed environment-variable injection and force-stop deadlocks. |
| 1.5.10 | 2026-07-15 | 라이트 모드 보조 버튼 대비와 Pretendard 기본 글꼴 크기를 개선했습니다. | Improved secondary-button contrast in light mode and adjusted the Pretendard base size. |
| 1.5.9 | 2026-07-15 | 전체 UI 글꼴을 Pretendard로 통일했습니다. | Standardized the UI font on Pretendard. |
| 1.5.8 | 2026-07-15 | 서버 실행 중 창 닫기 확인이 건너뛰어지던 문제를 수정했습니다. | Restored exit confirmation while a server is running. |
| 1.5.7 | 2026-07-15 | Job Object, 최대 10초 정상 종료 대기와 중복 종료 방지를 추가했습니다. | Added Job Objects, up to 10 seconds of graceful-shutdown waiting, and duplicate-stop prevention. |
| 1.5.6 | 2026-07-14 | WebP 콘텐츠 아이콘의 프록시 변환 호환성을 수정했습니다. | Fixed proxy conversion compatibility for WebP content icons. |
| 1.5.5 | 2026-07-14 | 느린 공유기를 위해 UPnP 연결·확인 제한 시간을 조정했습니다. | Adjusted UPnP connection and verification timeouts for slower routers. |
| 1.5.4 | 2026-07-14 | 콘텐츠 목록을 빠르게 탐색할 때 아이콘 로딩이 멈추던 교착을 수정했습니다. | Fixed an icon-loading deadlock during rapid content browsing. |
| 1.5.3 | 2026-07-14 | 직접 설정 중심 흐름, 월드 유형, 언어·테마 전환과 상태 표시 렌더링을 개선했습니다. | Improved direct setup, world type, language/theme switching, and status rendering. |
| 1.5.2 | 2026-07-14 | 간헐적 시작 충돌과 업데이트 안내 언어 선택을 수정했습니다. | Fixed an intermittent startup crash and localized update notes. |
| 1.5.1 | 2026-07-14 | 좁은 창의 관리 버튼, 다크 모드 입력 테두리와 업데이트 버전 판별을 수정했습니다. | Fixed narrow-window management buttons, dark input borders, and update version detection. |
| 1.5.0 | 2026-07-14 | 로컬 릴리스·코드 서명 도구와 둥근 UI 성능·접근성 개선을 추가했습니다. | Added local release and code-signing tools plus rounded-UI performance and accessibility improvements. |
| 1.4.0 | 2026-07-14 | Windows 기본 알림을 MineHarbor 공통 대화상자로 교체하고 버튼·키보드 동작을 통일했습니다. | Replaced native alerts with shared MineHarbor dialogs and standardized buttons and keyboard behavior. |
| 1.3.2 | 2026-07-14 | 업데이트 파일 최소 크기 제약을 완화하면서 구버전 호환 크기를 유지했습니다. | Relaxed update minimum-size checks while retaining legacy compatibility sizing. |
| 1.3.1 | 2026-07-14 | 작은 신규 런처 파일을 구버전이 거부하던 자동 업데이트 호환 문제를 수정했습니다. | Fixed old launchers rejecting smaller new update binaries. |
| 1.3.0 | 2026-07-14 | 수동 업데이트 확인, 버전 무시, 외부 Java 다운로드와 변경 이력 기반 릴리스 노트를 추가했습니다. | Added manual update checks, version ignore, external Java downloads, and changelog-driven release notes. |
| 1.2.1 | 2026-07-14 | UPnP COM을 STA 스레드로 고정하고 탐색·매핑 재시도와 결과 확인을 강화했습니다. | Moved UPnP COM to an STA thread and strengthened discovery, mapping retries, and verification. |
| 1.2.0 | 2026-07-13 | 관리 창을 모델리스로 전환하고 다중 서버 포트 충돌·상태 복구를 개선했습니다. | Made management windows modeless and improved multi-server port conflicts and state recovery. |
| 1.1.0 | 2026-07-13 | 빠른 명령 후보의 Tab·Shift+Tab·Enter 키보드 흐름을 개선했습니다. | Improved Tab, Shift+Tab, and Enter navigation for quick-command suggestions. |
| 1.0.0 | 2026-07-13 | MineHarbor 이름, 서버 휴지통과 기존 데이터·업데이트 호환 경로를 도입했습니다. | Introduced the MineHarbor name, server trash, and legacy data/update compatibility paths. |
| 0.4.2 | 2026-07-13 | 검색 가능한 3단계 빠른 명령 선택창과 테마 대응 스크롤을 추가했습니다. | Added a searchable three-level quick-command picker and theme-aware scrolling. |
| 0.4.1 | 2026-07-13 | 서버 관리 버튼의 한국어·영어 글자 잘림을 수정했습니다. | Fixed Korean and English text clipping in server-management buttons. |
| 0.4.0 | 2026-07-13 | 기본·사용자 빠른 명령과 루프백 전용 Paper/Purpur 실시간 브리지를 추가했습니다. | Added built-in and user quick commands plus an optional loopback-only Paper/Purpur live bridge. |
| 0.3.3 | 2026-07-12 | 내장 Paper JAR을 제거하고 공식 최신 빌드·SHA-256 검증 경로로 통일했습니다. | Removed the bundled Paper JAR and standardized on verified official latest builds. |
| 0.3.2 | 2026-07-12 | 벡터 아이콘, 테마 대응 컨트롤과 더 선명한 설정 계층을 도입했습니다. | Introduced vector icons, theme-aware controls, and a clearer setup hierarchy. |
| 0.3.1 | 2026-07-12 | 반응형 배치, 고대비·키보드·스크린 리더 접근성과 입력 검증을 개선했습니다. | Improved responsive layout, high-contrast, keyboard and screen-reader accessibility, and validation. |
| 0.3.0 | 2026-07-11 | 재현 가능한 Portable·설치 빌드, 데이터 위치 선택, 검증된 자동 업데이트와 릴리스 자동화를 도입했습니다. | Introduced reproducible portable/installer builds, selectable data storage, verified updates, and release automation. |
