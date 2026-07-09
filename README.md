# Paper 26.2 Server Launcher

Paper 26.2 기반 마인크래프트 서버를 Windows에서 쉽게 열기 위한 단일 실행 파일 런처입니다. Java 런타임, 서버 파일 준비, 최초 설정, 업데이트, 백업, 포트포워딩 점검을 최대한 자동화합니다.

[최신 EXE 다운로드](./releases/latest/download/Paper-26.2-Server.exe)

## 주요 기능

- Paper 26.2 서버 실행 자동화
- 내장 Java 25 런타임 사용
- 한국어/영어 UI 전환
- 다크/라이트 모드
- 최초 실행 서버 설정 마법사
- 자주 쓰는 서버 프리셋 제공
- 서버 메모리 설정
- `server.properties` 주요 항목 GUI 설정
- Paper 서버 파일 자동 업데이트
- 런처 자동 업데이트
- 서버 파일 교체 전 자동 백업
- 설정 변경 전 `server.properties` 백업
- 포트포워딩 및 외부 접속 가능 여부 점검
- 서버 소유자 자동 OP
- 필요할 때만 여는 내장 콘솔

## 빠른 시작

1. `Paper-26.2-Server.exe`를 원하는 폴더에 둡니다.
2. EXE를 실행합니다.
3. 최초 실행 설정창에서 서버 프리셋, 서버 이름, 최대 인원, 포트, 메모리, 서버 소유자를 정합니다.
4. Minecraft EULA에 동의합니다.
5. `서버 시작하기`를 누릅니다.
6. 런처에 표시되는 주소를 친구에게 전달합니다.

서버 데이터는 EXE가 있는 위치의 `Paper-26.2-Server-Data` 폴더에 생성됩니다.

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
- Paper 자동 업데이트
- 서버 소유자

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

## 업데이트와 백업

런처는 실행 시 최신 런처 버전을 확인합니다. 더 새 버전이 있으면 구버전 실행을 막고 업데이트를 적용합니다.

Paper 서버 파일은 같은 Minecraft 26.2 범위 안에서 최신 빌드를 확인하고 교체합니다. 서버 파일 교체 전에는 주요 서버 데이터가 자동으로 백업됩니다.

백업 위치:

- `Paper-26.2-Server-Data/server-backups`
- `Paper-26.2-Server-Data/paper-backups`
- `Paper-26.2-Server-Data/configuration-backups`

중요한 월드는 별도로 수동 백업하는 것을 권장합니다.

## 폴더 구조

```text
Paper-26.2-Server.exe
Paper-26.2-Server-Data/
  server.properties
  paper-26.2-server.jar
  world/
  plugins/
  server-backups/
  paper-backups/
  configuration-backups/
```

## 문제 해결

| 증상 | 확인할 것 |
| --- | --- |
| 친구가 접속하지 못함 | 포트포워딩, Windows 방화벽, 서버 포트, 공인 IP를 확인하세요. |
| 같은 집에서는 접속되는데 외부에서 안 됨 | 공유기 포트포워딩과 통신사 공유기/이중 NAT 여부를 확인하세요. |
| 서버가 바로 종료됨 | 콘솔을 열어 오류 로그를 확인하세요. |
| 월드 유형을 바꿨는데 적용되지 않음 | 이미 생성된 월드에는 월드 유형 변경이 적용되지 않습니다. 새 월드 생성 시 적용됩니다. |
| 자동 업데이트가 실패함 | 인터넷 연결과 GitHub/PaperMC 접속 가능 여부를 확인하세요. |
| 서버 소유자 자동 OP가 안 됨 | 정품 계정 인증이 켜져 있는지, 서버 소유자 닉네임이 정확한지 확인하세요. |

## 네트워크 사용

런처는 다음 작업을 위해 인터넷에 접속할 수 있습니다.

- 런처 최신 버전 확인
- Paper 26.2 최신 빌드 확인
- 업데이트 파일 다운로드
- 외부 접속 가능 여부 점검

## 현재 로컬 빌드

- 버전: `26.2.45.12`
- 파일명: `Paper-26.2-Server.exe`
- SHA256: `53BBAFC53BD48CC7952E933EF205DB67E06056443987F6CA19B4B5CAB6C9C35C`

---

# English

Paper 26.2 Server Launcher is a single-file Windows launcher for running a Paper 26.2 Minecraft server with minimal setup. It prepares Java, manages server files, provides a first-run setup wizard, checks updates, creates backups, and helps verify port forwarding.

[Download latest EXE](./releases/latest/download/Paper-26.2-Server.exe)

## Features

- Automated Paper 26.2 server startup
- Bundled Java 25 runtime
- Korean/English UI
- Dark/light mode
- First-run setup wizard
- Common server presets
- Server memory selection
- GUI configuration for major `server.properties` options
- Paper server auto update
- Launcher auto update
- Automatic backups before server file replacement
- `server.properties` backup before configuration changes
- Port-forwarding and external reachability check
- Server owner auto-OP
- Built-in console that can be opened only when needed

## Quick start

1. Put `Paper-26.2-Server.exe` in the folder where you want to run the server.
2. Run the EXE.
3. Choose a preset, server name, max players, port, memory, and server owner.
4. Accept the Minecraft EULA.
5. Press `Start server`.
6. Share the displayed address with friends.

Server data is created in `Paper-26.2-Server-Data` next to the EXE.

## Presets

- Peaceful survival
- Easy survival
- Normal survival
- Hard survival
- Hardcore survival
- Creative world, normal terrain
- Creative world, flat
- Custom

Creative presets automatically allow command blocks. Custom mode lets you choose game mode, difficulty, hardcore mode, and other settings manually.

## Server owner auto-OP

Set the Minecraft nickname in the `Server owner` field. When that player joins, the launcher plugin can grant OP automatically.

Online authentication is recommended. If online authentication is disabled, auto-OP is disabled to reduce nickname impersonation risk.

## Port forwarding

The default Minecraft server port is `25565/TCP`.

General setup:

1. Check the port shown in the launcher.
2. Allow inbound TCP traffic for that port in Windows Firewall.
3. Open your router admin page.
4. Add a port-forwarding rule.
5. Set both external and internal ports to the server port.
6. Set the internal IP to the IPv4 address of the server PC.
7. Use TCP as the protocol.
8. Start the server and check the launcher’s external reachability result.

External addresses usually look like `public-ip:port`. Some home networks cannot connect to their own public IP from inside the same network, so testing from a friend’s external network is the most reliable check.

## Updates and backups

The launcher checks for the latest launcher version on startup. If a newer launcher is published, the older launcher is blocked and the update is applied.

Paper server files are updated within the same Minecraft 26.2 line. Before replacing server files, the launcher creates backups of important server data.

Backup folders:

- `Paper-26.2-Server-Data/server-backups`
- `Paper-26.2-Server-Data/paper-backups`
- `Paper-26.2-Server-Data/configuration-backups`

Manual backups are still recommended for important worlds.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Friends cannot join | Check port forwarding, Windows Firewall, server port, and public IP. |
| Local network works but external access fails | Check router port forwarding and double NAT. |
| Server closes immediately | Open the console and read the error log. |
| World type did not change | Existing worlds keep their original type. The change applies to newly generated worlds. |
| Auto update fails | Check internet access and GitHub/PaperMC availability. |
| Owner auto-OP does not work | Check online authentication and the exact server owner nickname. |

## Current local build

- Version: `26.2.45.12`
- File name: `Paper-26.2-Server.exe`
- SHA256: `53BBAFC53BD48CC7952E933EF205DB67E06056443987F6CA19B4B5CAB6C9C35C`
