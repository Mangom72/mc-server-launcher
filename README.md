# Minecraft Server Launcher

Windows에서 마인크래프트 서버를 쉽게 열기 위한 단일 실행 파일 런처입니다. Java 런타임, 서버 파일 준비, 최초 설정, 업데이트, 백업, 포트포워딩 점검을 최대한 자동화합니다.

[최신 EXE 다운로드](./releases/latest/download/Paper-26.2-Server.exe)

## 주요 기능

- Paper, Vanilla, Purpur, Fabric, Forge, NeoForge, 직접 JAR 실행 지원
- Minecraft 버전 드롭다운 선택
- 스냅샷/프리릴리즈는 체크박스를 켰을 때만 표시
- 서버 프로필 생성·복제·가져오기·이름 변경·보관
- 여러 서버를 동시에 실행하고 상태·인원·콘솔을 확인하는 멀티 서버 대시보드
- 서버 파일 자동 다운로드
- 서버 파일 수동 JAR 지정
- 서버 업그레이드 버튼
- 서버 파일 자동 업데이트 옵션
- Minecraft/서버 종류에 맞는 Java 8·11·16·17·21·25 자동 선택
- 직접 JAR의 요구 Java 버전 선택
- 한국어/영어 UI 전환
- 다크/라이트 모드
- 최초 실행 서버 설정 마법사
- 자주 쓰는 서버 프리셋 제공
- 서버 메모리 설정
- `server.properties` 주요 항목 GUI 설정
- 런처 자동 업데이트
- 월드·설정·플러그인·모드를 포함한 전체 프로필 백업, SHA-256 검증 및 안전 복원
- 설정 변경 전 `server.properties` 백업
- 포트포워딩 및 외부 접속 가능 여부 점검
- 기존 포트포워딩 우선 검사와 실패 시에만 실행되는 UPnP 자동 매핑
- 서버 소유자 자동 OP
- 화이트리스트·OP·추방·차단 플레이어 관리
- Modrinth 플러그인·모드·데이터팩 검색 및 무결성 검증 설치
- 개인정보를 가린 진단 묶음과 콘솔 검색·WARN/ERROR 필터
- 필요할 때만 여는 내장 콘솔
- 창을 먼저 표시하고 업데이트·프로필·버전 목록을 백그라운드에서 불러오는 빠른 시작 방식
- Java·서버 파일·외부 접속 등 장시간 작업의 현재 단계와 진행률 표시

## 빠른 시작

1. `Paper-26.2-Server.exe`를 원하는 폴더에 둡니다.
2. EXE를 실행합니다.
3. 최초 실행 설정창에서 서버 프로필, 서버 종류, Minecraft 버전, 프리셋, 서버 이름, 포트, 메모리를 정합니다.
4. Minecraft EULA에 동의합니다.
5. `서버 시작하기`를 누릅니다.
6. 런처에 표시되는 주소를 친구에게 전달합니다.

서버 데이터는 EXE가 있는 위치의 `Minecraft-Servers-Data` 폴더에 생성됩니다.

앱은 메인 창을 먼저 표시한 다음 런처 업데이트와 서버 프로필을 백그라운드에서 확인합니다. 최초 설정창은 내장 버전 목록을 즉시 보여 주고, 최신 버전 목록을 뒤에서 갱신합니다. 화면 아래 로딩 영역에서 Java 준비, 서버 파일 다운로드, 업데이트, 서버 시작, 포트 확인, 외부 접속과 UPnP 단계를 확인할 수 있습니다.

## 서버 종류와 버전

지원되는 서버 종류:

- Paper
- Vanilla, Minecraft 기본 서버
- Purpur
- Fabric
- Forge
- NeoForge
- 직접 JAR 지정

버전 목록은 선택한 서버 종류에 맞는 공개 API에서 가져옵니다. 스냅샷/프리릴리즈는 기본적으로 숨겨져 있으며, `스냅샷/프리릴리즈 표시`를 켜면 목록에 포함됩니다.

직접 JAR을 선택하면 런처가 해당 파일을 프로필 폴더로 복사한 뒤 실행합니다. 제작자가 요구하는 Java 주 버전도 함께 고를 수 있습니다. 직접 JAR은 런처가 자동으로 교체하지 않습니다.

## 멀티 서버 프로필

메인 화면의 `서버 프로필`에서 새 서버 생성, 복제, 기존 서버 폴더 가져오기, 이름 변경, 안전 보관을 할 수 있습니다. 프로필마다 별도 폴더가 만들어지므로 월드, 플러그인, 모드와 설정을 따로 유지할 수 있습니다.

`멀티 서버`에서는 서로 다른 포트의 프로필을 동시에 실행할 수 있습니다. 각 서버는 별도 프로세스로 실행되며 상태, 실행 시간, 접속 인원, 콘솔을 확인할 수 있습니다. 비정상 종료 자동 재시작은 10분 안에 3번 연속 실패하면 중단됩니다.

폴더 예시:

```text
Paper-26.2-Server.exe
Minecraft-Servers-Data/
  .active-server-profile
  servers/
    기본 서버/
      server.properties
      world/
      plugins/
    creative-flat/
      server.properties
      world/
```

## 제공 프리셋

- 평화로움 야생
- 쉬움 야생
- 보통 야생
- 어려움 야생
- 하드코어 야생
- 크리에이티브 월드 일반 지형
- 크리에이티브 월드 평지
- 직접 설정

크리에이티브 프리셋은 명령 블록을 자동으로 허용합니다. 직접 설정을 고르면 게임 모드, 난이도, 하드코어 여부 등을 직접 조정할 수 있습니다.

## 설정되는 주요 서버 옵션

- 서버 프로필 이름
- 서버 종류
- Minecraft 버전
- 서버 이름
- 최대 접속 인원
- 서버 포트
- 서버 메모리
- 게임 모드
- 난이도
- 월드 유형
- PvP
- 화이트리스트
- 명령 블록
- 정품 계정 인증
- 시야 거리
- 시뮬레이션 거리
- 서버 파일 자동 업데이트
- 서버 소유자

## 서버 업그레이드와 백업

`서버 업글` 버튼을 누르면 현재 활성 프로필의 서버 파일을 선택한 서버 종류와 Minecraft 버전에 맞춰 최신 파일로 갱신합니다.

서버 종류나 Minecraft 버전을 바꾸면 기존 월드, 플러그인, 모드가 호환되지 않을 수 있습니다. 런처는 변경 전에 경고하고, 서버 데이터 백업을 만든 뒤 진행합니다. 중요한 월드는 별도로 수동 백업하는 것을 권장합니다.

`백업·복원`에서는 수동 백업, 보존 개수 설정, SHA-256 무결성 확인, 내보내기, 외부 백업 가져오기와 복원을 지원합니다. 복원 전에는 현재 상태를 한 번 더 백업하고, 임시 폴더에서 검증이 끝난 뒤 교체합니다.

백업 위치:

- `Minecraft-Servers-Data/servers/<프로필>/server-backups`
- `Minecraft-Servers-Data/servers/<프로필>/server-jar-backups`
- `Minecraft-Servers-Data/servers/<프로필>/configuration-backups`

## 콘텐츠와 플레이어 관리

`콘텐츠`에서는 현재 프로필의 Minecraft 버전과 서버 로더에 맞는 Modrinth 프로젝트를 검색합니다. 서버 측 사용 가능 여부를 확인하고, 다운로드 파일의 크기와 SHA-512/SHA-1 해시를 검증한 뒤 설치합니다. 설치 전 기존 콘텐츠는 자동 백업됩니다.

서버 실행 중 `플레이어`를 열면 화이트리스트 추가·제거, OP·DEOP, 추방, 차단·해제를 할 수 있습니다. 정품 계정 인증이 꺼진 서버에서는 닉네임 사칭 위험을 화면에 표시합니다.

## 콘솔과 진단

콘솔은 필요할 때만 펼쳐지며 검색, 전체/WARN/ERROR 필터, 줄 바꿈을 지원합니다. 서버가 오류로 종료되면 Java 불일치, 포트 충돌, 메모리 부족, 의존성 누락, 모드 비호환, 월드 다운그레이드 같은 흔한 원인을 요약합니다.

`진단 묶음`은 시스템 요약, 런처 설정, `server.properties`, 최근 로그와 크래시 보고서를 ZIP으로 묶습니다. 사용자 폴더 경로, IP 주소, RCON 비밀번호 등은 공유 전에 자동으로 가립니다.

## 서버 소유자 자동 OP

설정창의 `서버 소유자` 항목에 마인크래프트 닉네임을 입력하면, 해당 사용자가 서버에 접속할 때 자동으로 OP 권한을 받을 수 있습니다.

보안을 위해 정품 계정 인증이 켜진 상태를 권장합니다. 온라인 인증이 꺼져 있으면 닉네임 사칭 위험이 있으므로 자동 OP가 비활성화됩니다.

## 포트포워딩 안내

기본 마인크래프트 서버 포트는 `25565/TCP`입니다. 외부 접속을 허용하려면 공유기에서 이 포트를 서버 PC의 내부 IP로 포워딩해야 합니다.

일반적인 설정 순서:

1. 런처에서 서버 포트를 확인합니다.
2. Windows 방화벽에서 해당 TCP 포트의 인바운드 접속을 허용합니다.
3. 공유기 관리자 페이지에 접속합니다.
4. 포트포워딩 메뉴에서 외부 포트와 내부 포트를 같은 값으로 설정합니다.
5. 내부 IP에는 서버를 실행하는 PC의 IPv4 주소를 입력합니다.
6. 프로토콜은 TCP로 설정합니다.
7. 서버를 실행한 뒤 런처의 외부 접속 점검 결과를 확인합니다.

외부 접속 주소는 보통 `공인 IP:포트` 형식입니다. 같은 집 안에서는 공인 IP 접속이 안 될 수 있으므로, 외부 네트워크의 친구에게 접속 테스트를 부탁하는 것이 가장 확실합니다.

### 자동 외부 접속 처리 순서

1. 서버 실행 후 로컬 TCP 포트가 실제로 열렸는지 확인합니다.
2. 현재 공유기 설정으로 외부 접속이 가능한지 먼저 검사합니다.
3. 접속 가능하면 기존 포트포워딩을 그대로 사용하며 UPnP를 호출하지 않습니다.
4. 외부 접속이 실패한 경우에만 Windows UPnP NAT API로 공유기 장치를 검색합니다.
5. 같은 외부 포트와 프로토콜의 기존 매핑을 확인합니다. 다른 내부 PC를 가리키면 충돌로 처리하며 덮어쓰지 않습니다.
6. 충돌이 없으면 서버 포트의 TCP 매핑을 만들고, Minecraft Query가 켜진 경우 UDP도 시도합니다.
7. 외부 접속을 다시 검사하고, 여전히 실패하면 현재 내부 IPv4·게이트웨이·포트·방화벽·CGNAT 정보를 포함한 수동 안내 화면을 표시합니다.
8. 서버 종료 시 내부 IP, 내부 포트, 프로토콜과 세션 설명이 모두 일치하는 런처 생성 매핑만 삭제합니다.

외부 검사 서비스 자체가 응답하지 않으면 포트 실패로 단정하지 않으며 UPnP도 실행하지 않습니다. 기존 공유기 매핑은 변경하거나 삭제하지 않고, 방화벽 규칙도 자동으로 생성하지 않습니다.

## 네트워크 사용

런처는 다음 작업을 위해 인터넷에 접속할 수 있습니다.

- 런처 최신 버전 확인
- Minecraft 버전 목록 확인
- Paper, Purpur, Fabric, Forge, NeoForge, Vanilla 서버 파일 정보 확인
- Eclipse Adoptium 호환 Java 런타임 확인 및 다운로드
- Modrinth 콘텐츠 검색 및 다운로드
- 서버 파일 다운로드
- 업데이트 파일 다운로드
- 외부 접속 가능 여부 점검

## 문제 해결

| 증상 | 확인할 것 |
| --- | --- |
| 친구가 접속하지 못함 | 포트포워딩, Windows 방화벽, 서버 포트, 공인 IP를 확인하세요. |
| 같은 집에서는 접속되는데 외부에서 안 됨 | 공유기 포트포워딩과 통신사 공유기/이중 NAT 여부를 확인하세요. |
| 서버가 바로 종료됨 | 콘솔을 열어 오류 로그를 확인하세요. |
| 구버전 Paper가 도움말만 출력하고 종료됨 | `26.2.45.16` 이상을 사용하세요. 구버전 Paper에는 지원되지 않는 `--nogui` 인수를 전달하지 않습니다. |
| 한글 경로에서 구버전 Paper가 Java 에이전트 오류로 종료됨 | `26.2.45.18` 이상을 사용하세요. 서버 폴더 내부 JAR은 구형 Paperclip과 호환되는 상대 경로로 실행합니다. |
| 구버전을 골랐는데 최신 버전으로 저장됨 | `26.2.45.16` 이상에서 수정됐습니다. 백그라운드 버전 목록 갱신이 사용자의 선택을 덮어쓰지 않습니다. |
| 프로필 이름 변경 뒤 월드가 사라진 것처럼 보임 | `26.2.45.17` 이상에서는 프로필 폴더 전체를 안전하게 이동하며, 같은 이름의 기존 프로필을 덮어쓰지 않습니다. |
| Forge/NeoForge 서버 메모리 설정이 적용되지 않음 | `26.2.45.17` 이상에서는 `user_jvm_args.txt`의 활성 메모리 인수를 런처 설정과 동기화합니다. |
| 검색·설치·백업 중 창을 닫은 뒤 런처가 종료됨 | `26.2.45.17` 이상에서는 닫힌 창으로 향하는 비동기 UI 갱신을 안전하게 생략합니다. |
| 작업표시줄에 런처 아이콘이 표시되지 않음 | `26.2.45.19` 이상에서는 모든 런처 창에 EXE 아이콘과 고정 작업표시줄 식별자를 명시적으로 적용합니다. |
| 최신 버전에서 만든 월드를 구버전으로 바꾸려 함 | 월드 손상을 막기 위해 차단됩니다. 새 서버 프로필을 만든 뒤 구버전을 선택하세요. |
| 월드 유형을 바꿨는데 적용되지 않음 | 이미 생성된 월드에는 월드 유형 변경이 적용되지 않습니다. 새 월드 생성 시 적용됩니다. |
| 서버 종류나 버전을 바꾼 뒤 오류가 남 | 플러그인/모드 호환성을 확인하고, 필요한 경우 백업에서 복원하세요. |
| 자동 업데이트가 실패함 | 인터넷 연결과 선택한 서버 프로젝트 API 접속 가능 여부를 확인하세요. |
| 서버 소유자 자동 OP가 안 됨 | 정품 계정 인증이 켜져 있는지, 서버 소유자 닉네임이 정확한지 확인하세요. |

## 현재 로컬 빌드

- 버전: `26.2.45.19`
- 파일명: `Paper-26.2-Server.exe`

---

# English

Minecraft Server Launcher is a single-file Windows launcher for running Minecraft servers with minimal setup. It prepares Java, manages server files, provides a first-run setup wizard, checks updates, creates backups, and helps verify port forwarding.

[Download latest EXE](./releases/latest/download/Paper-26.2-Server.exe)

## Features

- Paper, Vanilla, Purpur, Fabric, Forge, NeoForge, and custom JAR support
- Minecraft version dropdown
- Snapshots/pre-releases are hidden unless explicitly enabled
- Create, clone, import, rename, and safely archive server profiles
- Multi-server dashboard with isolated processes, status, players, and consoles
- Automatic server file download
- Manual server JAR selection
- Server upgrade button
- Optional server file auto update
- Compatible Java 8/11/16/17/21/25 selection by Minecraft and server type
- Explicit Java requirement selection for custom JARs
- Korean/English UI
- Dark/light mode
- First-run setup wizard
- Common server presets
- Server memory selection
- GUI configuration for major `server.properties` options
- Launcher auto update
- Full-profile backups with SHA-256 verification and staged restore
- `server.properties` backup before configuration changes
- Port-forwarding and external reachability check
- Existing-forwarding-first checks with UPnP fallback only after a confirmed failure
- Server owner auto-OP
- Whitelist, OP, kick, and ban player controls
- Modrinth content search and verified installation
- Redacted diagnostic bundles and searchable WARN/ERROR console filters
- Built-in console that can be opened only when needed
- Main window first, with update/profile/version data loaded in the background
- Current-stage and progress indicators for Java, server files, startup, networking, and other long operations

## Quick start

1. Put `Paper-26.2-Server.exe` in the folder where you want to run the server.
2. Run the EXE.
3. Choose a profile, server type, Minecraft version, preset, server name, port, and memory.
4. Accept the Minecraft EULA.
5. Press `Start server`.
6. Share the displayed address with friends.

Server data is created in `Minecraft-Servers-Data` next to the EXE.

The main window appears before update and profile checks begin. The setup dialog shows a built-in version list immediately and refreshes it in the background. The loading area reports the current stage for Java preparation, server downloads, updates, process startup, local port checks, external reachability, and UPnP.

## Server types and versions

Supported server types:

- Paper
- Vanilla
- Purpur
- Fabric
- Forge
- NeoForge
- Custom JAR

Version lists are loaded from public APIs for the selected server type. Snapshots and pre-releases are hidden by default and shown only when the snapshot checkbox is enabled.

Custom JAR files are copied into the active profile folder before launch. You can select the Java major version required by the JAR. The launcher does not replace custom JARs automatically.

## Multi-server profiles

Use `Profiles` to create, clone, import, rename, activate, or safely archive servers. Each profile has its own folder, world, plugins, mods, and settings. `Multi-server` can run profiles on separate ports at the same time and stops crash loops after three failures in ten minutes.

## Upgrades and backups

The `Upgrade server` button updates the active profile’s server file for the selected server type and Minecraft version.

Changing server type or Minecraft version can break worlds, plugins, or mods. The launcher warns before saving and creates backups around server changes. The backup manager verifies SHA-256 manifests, makes a safety backup before restore, and replaces data only after staging validation. Manual off-device backups are still recommended for important worlds.

## Content, players, console, and diagnostics

The content manager searches Modrinth for projects matching the active Minecraft version and loader, then verifies file size and hashes before installation. While a server is running, the player manager can control whitelist, OP, kick, and ban commands.

The console supports search, WARN/ERROR filters, and wrapping. Common startup failures are summarized automatically. Diagnostic bundles redact user paths, IP addresses, and sensitive configuration values before packaging recent logs and crash reports.

## Network use

The launcher may access the internet to:

- Check launcher updates
- Load Minecraft version lists
- Resolve Paper, Purpur, Fabric, Forge, NeoForge, and Vanilla server downloads
- Resolve compatible Eclipse Adoptium Java runtimes
- Search and download Modrinth content
- Download server files
- Download launcher updates
- Verify external reachability

## Automatic external access flow

After the local TCP listener is confirmed, the launcher checks existing external reachability first. A working router configuration is left untouched and no UPnP call is made. Only a confirmed external failure triggers Windows UPnP NAT discovery and collision checks.

The launcher never overwrites a mapping owned by another internal client. It creates TCP for the server port and optionally UDP when Minecraft Query is enabled, then checks external access again. If the check still fails, the manual guide shows the PC IPv4 address, gateway, internal/external ports, router page, firewall status, and possible double NAT or CGNAT.

Only mappings created by the current launcher session and still matching the recorded internal client, port, protocol, and unique description are removed when the server stops. Existing router mappings and Windows Firewall rules are not modified.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Friends cannot connect | Check port forwarding, Windows Firewall, server port, and public IP. |
| LAN works but external access fails | Check router port forwarding and double NAT. |
| Server exits immediately | Open the console and check the error log. |
| Legacy Paper prints help and exits | Use `26.2.45.16` or newer. Unsupported `--nogui` is no longer passed to legacy Paper. |
| Legacy Paper exits with a Java agent error in a non-ASCII path | Use `26.2.45.18` or newer. JARs inside the server directory are launched with a legacy Paperclip-compatible relative path. |
| An older version selection is saved as the latest version | Fixed in `26.2.45.16`; background list refresh now preserves the user selection. |
| A world seems missing after renaming a profile | Version `26.2.45.17` and newer move the complete profile directory and never overwrite an existing profile with the same name. |
| Forge/NeoForge ignores the configured memory | Version `26.2.45.17` and newer synchronize active memory arguments in `user_jvm_args.txt`. |
| The launcher exits after closing a search, install, or backup window | Version `26.2.45.17` and newer safely discard asynchronous UI updates targeting a closed window. |
| The launcher icon is missing from the taskbar | Version `26.2.45.19` and newer explicitly apply the EXE icon and a stable taskbar identity to every launcher window. |
| Trying to use a newer world on an older server | The downgrade is blocked to prevent world damage. Create a new server profile for the older version. |
| World type change does not apply | Existing worlds keep their generated type. Create a new world to apply it. |
| Errors after changing server type/version | Check plugin/mod compatibility and restore from backup if needed. |
| Auto update fails | Check internet access and selected server project API availability. |
| Owner auto-OP does not work | Check online authentication and the exact owner nickname. |

## Current local build

- Version: `26.2.45.19`
- File name: `Paper-26.2-Server.exe`
