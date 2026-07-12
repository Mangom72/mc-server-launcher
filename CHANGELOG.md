# Changelog

이 프로젝트는 제품 버전에 [Semantic Versioning](https://semver.org/)을 사용합니다. `26.2.45.xx` 값은 별도의 내부 빌드 번호입니다.

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

Version `0.3.1` improves responsive action layouts, light/dark/high-contrast readability, keyboard and screen-reader accessibility, inline setup validation, loading and disabled states, content error recovery, and stable server-list updates without selection flicker.

Version `0.3.0` introduces reproducible Portable and installer builds, selectable persistent data storage, separate semantic product and internal build versions, approved and verified launcher updates with rollback, release automation, policy documents, and core regression tests. Existing server data structures and non-destructive Portable data discovery are preserved.
