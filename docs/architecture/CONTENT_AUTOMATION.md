# 콘텐츠·자동화 구조 / Content and automation architecture

## 서버별 저장 파일

- `.mineharbor/content-manifest.json`: MineHarbor가 설치한 프로젝트 ID, 버전, 해시, 상대 경로, 종류, 로더, 월드와 의존성
- `.mineharbor/automation.json`: 시작/종료 백업 훅, 백업 보존 정책, 예약 작업, 다음 실행과 최근 결과, 실행 임대
- `.mineharbor/disabled`: 활성화 해제한 관리 콘텐츠
- `.mineharbor/content-backups`: 업데이트 전 파일
- `.mineharbor/content-trash`: 제거한 관리 콘텐츠의 복구 가능 보관 위치

manifest에 없는 플러그인·모드·데이터팩은 수동 설치로 표시합니다. 상대 경로는 항상 서버 루트 안으로 해석되는지 검사하며, JSON은 임시 파일을 검증한 뒤 원자적으로 교체합니다. 손상된 원본은 자동 수정하거나 덮어쓰지 않습니다.

## 콘텐츠 흐름

Modrinth 검색은 콘텐츠 종류, Minecraft 버전과 로더 facet을 사용합니다. 설치 전에 정확히 호환되는 release 파일을 선택하고 필수 의존성을 재귀적으로 확인하며 순환 의존성을 거부합니다. manifest를 읽고 쓸 때도 프로젝트 중복과 의존성 순환을 다시 검사하고, 다른 설치 콘텐츠가 사용하는 필수 의존성은 먼저 소비자를 제거하지 않는 한 삭제하지 않습니다. 파일은 허용된 HTTPS CDN에서 제한된 크기로 내려받아 SHA-512 또는 SHA-1을 확인한 뒤 최종 위치로 이동합니다.

데이터팩은 선택한 월드의 `datapacks`에만 설치합니다. ZIP의 루트 `pack.mcmeta`, 양의 `pack_format`, 경로 이탈·절대 경로·드라이브/대체 데이터 스트림 표기·`.`/`..` 세그먼트·중복 항목, 파일 수와 총 해제 크기를 검사합니다.

## 자동화 흐름

예약 검사기는 실행 시각이 지난 작업을 디스크 잠금 안에서 `Running`, `LeaseUtc`, 프로세스 ID와 프로세스 시작 시각으로 먼저 기록합니다. 같은 프로세스의 다음 검사나 다시 열린 화면은 임대된 작업을 다시 실행하지 않습니다. 소유 프로세스가 종료된 임대는 즉시 복구하고, 프로세스 정보가 없는 이전 형식의 임대만 30분 후 복구합니다. 완료·실패·취소는 최근 결과와 다음 실행 시각을 원자적으로 저장합니다.

일정 검사는 주 MineHarbor 창 또는 다중 서버 관리 창이 실행 중일 때 동작합니다. 이 버전은 Windows 서비스나 로그온 전 백그라운드 에이전트를 설치하지 않으므로 앱이 완전히 종료된 동안에는 새 작업을 시작하지 않습니다. 다음 실행 시 앱이 열리면 실행 시각이 지난 활성 작업을 한 번만 청구합니다.

장시간 백업과 상태 수집은 `Task`와 `CancellationToken`을 사용합니다. UI가 닫히면 토큰을 취소하고, 완료 콜백은 폼이 폐기되었는지 다시 확인합니다. 대시보드는 얻을 수 없는 CPU, Java, 외부 접속과 TPS/MSPT를 추정하지 않고 지원 불가 또는 미확인으로 표시합니다.

## English summary

Per-server manifests distinguish managed and manual content, constrain all paths to the server root, and use validated atomic JSON replacement. Modrinth installation enforces exact game/loader compatibility, required dependencies, bounded HTTPS downloads, and hashes. While a MineHarbor management window is running, automation claims jobs on disk before execution, recovers expired leases, records results, and applies count/day/size backup retention; no Windows background service is installed. Long work is cancellable, and the dashboard reports unsupported data instead of guessing.
