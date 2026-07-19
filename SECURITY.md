# Security Policy

## 지원 버전

보안 수정은 최신 안정 릴리스를 우선 대상으로 합니다. 오래된 빌드에서 문제가 발생하면 최신 릴리스에서도 재현되는지 먼저 확인해 주세요.

## 취약점 제보

민감한 취약점은 공개 Issue에 세부 내용을 올리지 말고 저장소의 [비공개 Security Advisory](https://github.com/Mangom72/MineHarbor/security/advisories/new)로 제보해 주세요. 재현 조건, 영향을 받는 버전, 예상 영향과 가능한 완화 방법을 포함하면 확인에 도움이 됩니다.

서버 월드, 계정 정보, 공인 IP, 공유기 설정, 토큰 또는 개인 로그 원본은 제보에 포함하지 마세요. 필요한 경우 런처가 생성한 개인정보 제거 진단 묶음을 사용해 주세요.

명령 브리지는 루프백 주소에만 바인딩된 런처 리스너, 실행마다 새로 생성되는 256비트 토큰, 프로필·프로토콜 확인, JSON Lines 크기와 후보 개수 제한을 사용합니다. 브리지는 서버 명령을 실행하지 않으며 외부 네트워크 포트를 열지 않습니다. 세션 파일이나 토큰이 로그·진단 묶음·취약점 제보에 포함되지 않게 해 주세요.

## Supported versions

Security fixes target the latest stable release. Please verify whether an issue still reproduces on the latest release before reporting it.

Report sensitive vulnerabilities through a [private GitHub Security Advisory](https://github.com/Mangom72/MineHarbor/security/advisories/new), not a public issue. Do not include worlds, account data, public IP addresses, router credentials, tokens, or unredacted logs.

The command bridge uses a loopback-only launcher listener, a fresh 256-bit token per run, profile and protocol validation, and bounded JSON Lines messages and suggestions. It never executes server commands or opens an external network port. Do not include bridge session files or tokens in reports.
