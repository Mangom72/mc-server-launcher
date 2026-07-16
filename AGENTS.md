# MineHarbor AI Collaboration Guidelines

이 문서는 MineHarbor 프로젝트에서 활동하는 모든 AI 도구와 작업자가 준수해야 하는 공통 진입점 및 핵심 안전 규칙입니다.

## 1. 작업 시작 시 읽을 파일

작업을 시작하기 전, 다음 기존 협업 문서들을 반드시 확인하세요.

1. `.agents/AGENTS.md`
2. `docs/ai/SYNC_STATE.md`
3. `docs/ai/CODEX_HANDOFF.md`
4. `CONTRIBUTING.md`

## 2. 기본 개발 명령

```powershell
.\scripts\Prepare-BuildResources.ps1
.\build.ps1
.\test.ps1
```

## 3. 안전 규칙

- `main` 브랜치에 직접 커밋하거나 푸시하지 않는다.
- 작업 시작 전에 `git status`와 현재 브랜치를 확인한다.
- 작업 후 `git diff`, 빌드 및 테스트 결과를 확인한다.
- 작업 공간 밖의 파일을 사용자 승인 없이 수정하거나 삭제하지 않는다.
- `git reset --hard`, `git clean -fd`, 강제 푸시는 사용자 승인 없이 실행하지 않는다.
- 실제 사용자 서버 데이터, 공유기 설정, UPnP 매핑 및 외부 포트를 테스트에서 변경하지 않는다.
- UI 문구를 변경하면 한국어와 영어를 함께 수정한다.
- 다운로드 기능은 HTTPS, 허용 호스트, 파일 크기 및 해시 검증을 유지한다.
- 관련 없는 대규모 포맷팅을 하지 않는다.
- 비밀키, 토큰, 인증서 및 개인정보를 저장소에 추가하지 않는다.
- 제품 및 빌드 버전은 `version.json`을 단일 기준으로 사용한다.
- 작업 종료 또는 다른 AI에게 인계할 때는 원칙적으로 `docs/ai/SYNC_STATE.md`를 갱신한다.
