# 개인정보 처리 안내 / Privacy

## 한국어

MineHarbor — Minecraft Server Launcher는 자동 사용 통계나 분석 정보를 수집하지 않으며, 오류 로그나 진단 묶음을 자동으로 전송하지 않습니다. 진단 묶음은 사용자가 직접 생성하고 공유할 때만 외부로 전달됩니다.

런처는 다음 기능을 위해 네트워크에 접속합니다.

- GitHub Releases: 런처 업데이트 정보와 사용자가 승인한 업데이트 파일 다운로드
- PaperMC Fill API, Purpur API: 서버 버전과 서버 JAR 확인 및 다운로드
- Mojang 버전 메타데이터 및 다운로드 서버: Vanilla 서버 버전과 파일 확인
- Fabric Meta, Forge 파일/Maven, NeoForge Maven: 해당 서버 로더 확인 및 다운로드
- Eclipse Adoptium API와 GitHub 릴리스 자산: 호환 Java 런타임 확인 및 다운로드
- Modrinth API/CDN: 플러그인과 모드 검색, 아이콘 표시, 선택한 콘텐츠 다운로드
- portchecker.io: 공인 IP 확인과 사용자가 시작한 외부 포트 접속 가능 여부 검사
- 로컬 공유기 UPnP: 외부 접속 검사 실패 후에만 자동 포트 매핑 시도
- playit.gg 문서: 사용자가 해당 안내 버튼을 선택했을 때 브라우저로 열기

Paper/Purpur 실시간 명령 브리지는 인터넷이나 LAN에 연결하지 않습니다. 런처가 실행마다 만든 임시 포트의 `127.0.0.1` 리스너에만 연결하며, 무작위 세션 토큰·프로필 이름·프로토콜 버전을 확인합니다. 세션 파일과 토큰은 서버가 종료되면 삭제되고 진단 묶음에 포함되지 않으며 로그에도 기록하지 않습니다. 브리지 JAR 설치·업데이트를 사용자가 선택한 경우에만 GitHub Release에서 자산을 내려받아 크기와 SHA-256을 검증합니다.

공인 IP와 서버 포트는 외부 접속 가능 여부를 확인하기 위해 portchecker.io에 요청될 수 있습니다. 런처는 공유기 관리 비밀번호를 읽거나 전송하지 않습니다.

진단 묶음에는 운영체제 버전, 런처 제품 버전과 빌드 번호, CPU 논리 코어 수, 총 메모리, 서버 종류·Minecraft 버전·설정 파일, 최근 서버 로그와 최대 3개의 충돌 보고서가 포함될 수 있습니다. 사용자 프로필 경로, 서버 절대 경로, IPv4 주소, 서버 소유자 이름, RCON 비밀번호, 서버 IP와 일부 민감 설정은 제거하거나 대체합니다. 파일 크기가 지나치게 크거나 재분석 지점인 파일은 포함하지 않습니다.

자동 업데이트는 `Mangom72/MineHarbor` GitHub Release에서만 내려받고, 메타데이터의 크기와 SHA-256을 모두 확인합니다. 코드 서명은 릴리스 워크플로에 인증서가 설정된 경우에만 적용됩니다.

## English

MineHarbor — Minecraft Server Launcher does not collect analytics or usage telemetry. It does not automatically upload errors, logs, or diagnostic bundles. A diagnostic bundle leaves the computer only when the user explicitly shares it.

The launcher accesses GitHub Releases for launcher updates; PaperMC, Purpur, Mojang, Fabric, Forge, and NeoForge services for server metadata and files; Eclipse Adoptium and its GitHub release assets for Java; Modrinth for content search, icons, and downloads; and portchecker.io for public-IP and external-port checks initiated by the launcher. UPnP discovery happens only after an external connection check fails. The launcher does not read or transmit router administrator passwords.

Diagnostic bundles may include OS information, launcher product/build versions, logical processor count, total memory, server type and version, redacted settings, recent logs, and up to three crash reports. User-profile paths, absolute server paths, IPv4 addresses, owner names, RCON passwords, server IP values, and selected sensitive settings are removed or replaced. Oversized files and reparse points are excluded.

Launcher updates are downloaded only from the `Mangom72/MineHarbor` GitHub Release path and are checked against both the declared size and SHA-256. Code signing is applied only when a certificate is configured in the release workflow.

The optional Paper/Purpur command bridge never connects to the internet or LAN. It connects only to the launcher's temporary `127.0.0.1` listener and validates a fresh random session token, profile, and protocol version. The session file is deleted when the server stops, the token is not logged, and the session file is excluded from diagnostic bundles. The bridge JAR is downloaded from the GitHub Release only after user consent and is verified by size and SHA-256.
