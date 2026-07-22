# Security Policy

## 지원 버전

보안 수정은 최신 안정 릴리스를 우선 대상으로 합니다. 오래된 빌드에서 문제가 발생하면 최신 릴리스에서도 재현되는지 먼저 확인해 주세요.

## 취약점 제보

민감한 취약점은 공개 Issue에 세부 내용을 올리지 말고 저장소의 [비공개 Security Advisory](https://github.com/Mangom72/MineHarbor/security/advisories/new)로 제보해 주세요. 재현 조건, 영향을 받는 버전, 예상 영향과 가능한 완화 방법을 포함하면 확인에 도움이 됩니다.

서버 월드, 계정 정보, 공인 IP, 공유기 설정, 토큰 또는 개인 로그 원본은 제보에 포함하지 마세요. 필요한 경우 런처가 생성한 개인정보 제거 진단 묶음을 사용해 주세요.

명령 브리지는 루프백 주소에만 바인딩된 런처 리스너, 실행마다 새로 생성되는 256비트 토큰, 프로필·프로토콜 확인, JSON Lines 크기와 후보 개수 제한을 사용합니다. 브리지는 서버 명령을 실행하지 않으며 외부 네트워크 포트를 열지 않습니다. 세션 파일이나 토큰이 로그·진단 묶음·취약점 제보에 포함되지 않게 해 주세요.

플러그인과 모드는 서버에서 코드를 실행할 수 있으므로 신뢰하는 프로젝트만 설치해야 합니다. MineHarbor는 Modrinth CDN·크기·SHA-512/SHA-1, Minecraft 버전·로더와 필수 의존성을 확인하지만 제3자 콘텐츠 자체의 안전성을 보증하지 않습니다. 데이터팩 ZIP은 루트 `pack.mcmeta`, 경로 이탈, 중복 항목, 항목 수와 해제 크기를 검사합니다. 관리 콘텐츠 제거는 서버 내부 휴지통으로 이동하며 수동 파일은 manifest에서 명확히 구분합니다.

`.mineharbor/content-manifest.json`과 `.mineharbor/automation.json`은 크기·스키마·경로·중복을 검증하고 원자적으로 교체합니다. 손상된 설정은 자동으로 덮어쓰지 않습니다. 예약 명령은 줄바꿈과 제어 문자를 거부하고, 예약 작업은 임대 상태를 저장해 중복 실행을 막습니다. 자동화 파일을 편집할 수 있는 사용자는 서버 콘솔 명령을 예약할 수 있으므로 서버 폴더의 Windows 권한을 신뢰할 수 있는 계정으로 제한해 주세요.

현재 GitHub Release의 Windows 실행 파일은 릴리스마다 새로 생성하고 즉시 폐기하는 자체서명 인증서를 사용합니다. 이는 다운로드 후 파일이 변경되지 않았는지 확인하는 보조 수단이며, 공개 인증 기관이 MineHarbor 배포자 신원을 보증한다는 뜻이 아닙니다. 인증서를 신뢰 저장소에 수동 설치하지 말고, GitHub Release의 `SHA256SUMS.txt`와 저장소 출처를 함께 확인해 주세요.

## Supported versions

Security fixes target the latest stable release. Please verify whether an issue still reproduces on the latest release before reporting it.

Report sensitive vulnerabilities through a [private GitHub Security Advisory](https://github.com/Mangom72/MineHarbor/security/advisories/new), not a public issue. Do not include worlds, account data, public IP addresses, router credentials, tokens, or unredacted logs.

The command bridge uses a loopback-only launcher listener, a fresh 256-bit token per run, profile and protocol validation, and bounded JSON Lines messages and suggestions. It never executes server commands or opens an external network port. Do not include bridge session files or tokens in reports.

Plugins and mods can execute code in the server process, so install only trusted projects. MineHarbor validates the Modrinth CDN, declared size, SHA-512/SHA-1, game version, loader, and required dependencies, but cannot guarantee third-party content safety. Data-pack ZIPs are checked for a root `pack.mcmeta`, path traversal, duplicate entries, entry count, and expanded size. Managed removals move files into server-local trash, and manually installed files remain clearly distinguished.

`.mineharbor/content-manifest.json` and `.mineharbor/automation.json` are bounded, schema/path/duplicate validated, and atomically replaced; corrupt files are not silently overwritten. Scheduled commands reject line breaks and control characters, and execution leases prevent duplicate jobs. Anyone who can edit a server's automation file can schedule console commands, so restrict Windows permissions on server directories to trusted accounts.

Windows executables in the current GitHub Release use a fresh self-signed certificate that is discarded after each release. This is an additional file-integrity signal, not a public certificate authority's verification of the MineHarbor publisher identity. Do not manually install that certificate as a trusted root; verify the repository source and release `SHA256SUMS.txt` instead.
