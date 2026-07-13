# Changelog

이 프로젝트는 제품 버전에 [Semantic Versioning](https://semver.org/)을 사용합니다. `26.2.45.xx` 값은 별도의 내부 빌드 번호입니다.

## [0.4.1] - 2026-07-13

내부 빌드: `26.2.45.29`

### Fixed

- 서버 관리 화면의 아이콘과 번역 문구를 실제 글꼴 기준으로 측정해 버튼 글자가 잘리거나 말줄임표로 표시되던 문제 수정
- 상단 실행 버튼을 앞 버튼의 실제 폭에 맞춰 배치하고 프로필 관리의 긴 가져오기 문구를 간결하게 통일
- 한국어·영어 서버 관리 버튼의 최소 표시 폭을 확인하는 자동 회귀 테스트 추가

## [0.4.0] - 2026-07-13

내부 빌드: `26.2.45.28`

### Added

- 서버 관리·플레이어·화이트리스트·월드·정보 카테고리의 기본 빠른 명령과 실행 전 위험 확인
- `config/quick-commands.json`에 별도로 보존되는 사용자 명령 템플릿 추가·수정·삭제 화면
- 현재 커서, 따옴표, 온라인 플레이어, 명령 기록을 반영하는 125ms 디바운스 로컬 자동완성
- Paper/Purpur 1.13 이상에서 공개 CommandMap API로 명령·별칭·설명·usage·플러그인·탭 완성을 제공하는 선택형 Java 브리지
- 실행별 256비트 토큰, 프로필 확인, 루프백 전용 임시 포트와 크기 제한 JSON Lines 통신
- 브리지 설치 동의 기본값, 상태·버전·프로토콜·발견 명령 표시와 검증된 설치·업데이트·제거
- 브리지 프로토콜 단위 테스트와 임시 Paper 서버 명령 목록·자동완성·플레이어·재연결 통합 테스트

### Changed

- 메인 하단을 콘솔과 빠른 명령이 함께 사용할 수 있는 반응형 작업 영역으로 확장
- 기본 창 높이를 조정해 빠른 명령을 추가한 뒤에도 기존 버튼과 입력창이 잘리지 않도록 개선
- 릴리스 자산과 `update.json`에 브리지 JAR의 버전, 프로토콜, 호환 Minecraft 범위, 크기와 SHA-256 포함

### Security

- 브리지 연결은 외부 주소를 허용하지 않고 세션 토큰을 로그나 진단 묶음에 포함하지 않음
- 브리지는 명령을 실행하지 않으며 실제 전송은 기존 서버 콘솔 입력 경로만 사용
- 런처가 관리 기록을 가진 정확한 JAR만 제거하고 업데이트 실패 시 이전 JAR 복원

## [0.3.3] - 2026-07-12

내부 빌드: `26.2.45.27`

### Changed

- Paper 서버 JAR 내장과 최초 실행 전용 고정 빌드 경로를 제거하고 공식 API 최신 빌드 다운로드와 SHA-256 검증 경로로 통합
- 새 서버의 자동 업데이트 기본값을 활성화하고 최초 다운로드 직후 같은 파일을 다시 확인하던 중복 다운로드 제거
- 설정 화면 콘텐츠 폭과 최소 창 너비를 맞춰 DPI 배율에 따라 나타나던 가로 스크롤 제거
- 한국어·영어 작업 버튼을 짧고 안정적인 문구로 통일하고 전체 동작 설명은 도움말과 접근성 설명으로 유지
- 최신 Paper/Purpur에서 GUI 콘솔에 맞는 `--nojline --nogui` 인수를 사용하도록 개선

### Added

- 콘솔의 일반 경고, Java·터미널 호환성 경고, 오류를 구분하는 필터와 색상
- 내장 Paper 리소스 부재, 호환성 경고 분류, 버전별 콘솔 인수와 설정 화면 폭을 확인하는 자동 회귀 테스트

## [0.3.2] - 2026-07-12

내부 빌드: `26.2.45.26`

### Changed

- 메인 화면을 서버 제어와 관리 도구 영역으로 나누고 시각적 위계를 강화
- 주요 동작, 서버 관리, 백업과 콘텐츠 화면에 동일한 선 굵기의 코드 드로잉 벡터 아이콘 적용
- 보조 버튼에 얇은 테두리를 추가해 카드와 버튼의 경계를 라이트·다크 모드에서 명확하게 표시
- 설정 화면의 기본 흰색 콤보박스를 테마 대응형 오너 드로우 컨트롤로 교체
- 빠른 설정, 기본 정보, 서버 규칙 섹션을 명확히 구분하고 프리셋에 따라 불필요한 공간 자동 축소
- 선택한 프리셋에 색상 외 체크 표시를 추가하고 스냅샷·Java 문구를 간결하게 정리

### Added

- DPI 배율과 테마에 맞춰 직접 렌더링되는 21종 버튼 아이콘 체계

## [0.3.1] - 2026-07-12

내부 빌드: `26.2.45.25`

### Changed

- 메인 작업 버튼을 창 너비에 맞게 정렬하고 기본 창 높이를 실제 콘텐츠에 맞게 조정
- 라이트·다크·Windows 고대비 모드의 텍스트, 경계선과 상태 색상 대비 개선
- 주소 복사와 명령 전송 버튼의 활성 상태, 로딩 커서와 상태 안내를 실제 동작과 일치하도록 개선
- 설정 화면의 입력 오류를 관련 필드 옆과 하단에 표시하고 중복 경고 창 제거
- 설정 화면의 불필요한 가로 스크롤 제거 및 작은 화면에서는 세로 스크롤만 사용
- 콘텐츠 설치 실패 메시지에 재시도 경로를 제공하고 중복 팝업 제거
- 서버 관리 목록을 전체 재생성하지 않고 갱신해 깜빡임과 선택 위치 이동 방지

### Added

- F5 시작, Shift+F5 안전 종료, Ctrl+, 설정, Ctrl+K 콘솔 검색 단축키
- 입력 필드와 상태 영역의 접근성 이름·설명, 명확한 2px 키보드 포커스 표시
- UX 대비, 접근성 역할과 비활성 버튼 커서를 확인하는 자동 회귀 테스트

## [0.3.0] - 2026-07-11

내부 빌드: `26.2.45.24`

### Added

- Portable 및 Windows 설치 프로그램 배포 구조
- 사용자 데이터, Portable, 사용자 지정 서버 데이터 위치 선택과 영구 저장
- 단일 `version.json` 기반 제품 버전·빌드 번호 생성
- `update.json`, `SHA256SUMS.txt`와 GitHub Actions 릴리스 자동화
- MIT 라이선스, 보안 정책, 개인정보 안내와 기여 문서
- 데이터 위치, 업데이트 메타데이터, 해시, 교체 및 설정 UI 회귀 테스트

### Changed

- 런처 업데이트를 사용자 승인 후에만 실행하도록 변경
- 다운로드 크기와 SHA-256 검증, 기존 EXE 백업, 새 실행 확인과 복구 절차 보강
- 최초 설정 화면에 작은 화면 스크롤, 고정 저장/취소 버튼, 근접 입력 오류 표시와 비활성화 이유 도움말 추가

### Preserved

- 서버 프로필, 월드, 백업, 콘텐츠, Java 런타임과 포트포워딩/UPnP 기존 데이터 구조
- 기존 `Minecraft-Servers-Data` 자동 감지 및 비파괴 사용

## English

Version `0.4.1` prevents Korean and English labels from being clipped inside server-management buttons by measuring icon and text space, aligning adjacent actions from their actual bounds, and adding regression coverage for every management label.

Version `0.4.0` adds built-in and user quick commands, cursor-aware local completion, command history and safety confirmation, plus an optional loopback-only Paper/Purpur bridge for live command metadata, console tab completion, and online players. Bridge installation requires per-profile consent, verifies release metadata and SHA-256, never executes commands, and preserves user data on removal.

Version `0.3.3` removes the bundled Paper server JAR, unifies first-run preparation with the verified official latest-build path, prevents duplicate downloads, fixes setup horizontal scrolling, shortens bilingual action labels, and separates compatibility notices from actionable warnings and errors.

Version `0.3.2` introduces a consistent vector icon system, clearer server-control and management hierarchy, bordered secondary actions, fully themed owner-drawn combo boxes, denser setup sections, and non-color preset selection indicators.

Version `0.3.1` improves responsive action layouts, light/dark/high-contrast readability, keyboard and screen-reader accessibility, inline setup validation, loading and disabled states, content error recovery, and stable server-list updates without selection flicker.

Version `0.3.0` introduces reproducible Portable and installer builds, selectable persistent data storage, separate semantic product and internal build versions, approved and verified launcher updates with rollback, release automation, policy documents, and core regression tests. Existing server data structures and non-destructive Portable data discovery are preserved.
