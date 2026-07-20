# .NET 현대화 검토 / .NET modernization review

## 결정

MineHarbor 1.6.0은 실행 타깃을 .NET Framework 4.x로 유지하면서 SDK 스타일 `MineHarbor.csproj`를 병행 도입합니다. 프로젝트는 `net48`을 대상으로 하며 기존 `build.ps1`과 같은 소스 목록을 컴파일합니다. PR과 `main` CI는 기존 Portable 빌드와 SDK 스타일 빌드를 모두 실행합니다.

즉시 최신 .NET 런타임으로 바꾸지 않은 이유는 다음과 같습니다.

- 기존 자동 업데이트는 단일 Windows EXE, 파일 버전 리소스와 현재 설치 프로그램 배치를 전제로 합니다.
- WinForms 화면, Windows Job Object, 방화벽/네트워크 진단과 Windows COM 기반 UPnP 백업 경로의 동작을 다시 검증해야 합니다.
- `System.Web.Script.Serialization`과 일부 .NET Framework 전용 API를 먼저 교체해야 합니다.
- 지원 중인 기존 Windows 설치가 새 런타임 배포 방식 때문에 시작되지 않는 회귀를 피해야 합니다.

Microsoft의 [WinForms 마이그레이션 지침](https://learn.microsoft.com/dotnet/desktop/winforms/migration/)에 따라 SDK 스타일 변환을 먼저 수행하고 API·종속성 차이를 단계적으로 감사합니다. 2026년 7월 기준 [.NET 지원 정책](https://learn.microsoft.com/dotnet/core/releases-and-support)에 따르면 .NET 10은 2028년 11월까지 지원되는 LTS이므로 다음 런타임 후보로 사용합니다.

## 다음 단계

1. JSON 저장을 `System.Text.Json` 호환 계층으로 분리하고 schema/원자적 저장 테스트를 유지합니다.
2. 프로세스, 네트워크, WinForms 어댑터의 Windows 전용 경계를 명시합니다.
3. `net48`와 `net10.0-windows` 다중 타깃 실험 브랜치에서 UI, 업데이트, 설치, Job Object와 UPnP를 검증합니다.
4. 자체 포함 단일 파일 크기, 시작 속도와 자동 업데이트 교체/복구를 실제 설치 환경에서 검증합니다.
5. 동등한 Portable EXE·설치 프로그램·브리지 테스트를 통과한 뒤에만 기본 런타임을 전환합니다.

## Decision

MineHarbor 1.6.0 retains its .NET Framework runtime target while adding an SDK-style `net48` project. CI builds both the legacy Portable path and the SDK-style compatibility path. The next runtime candidate is .NET 10 LTS, but a default-target switch is deferred until framework-only serialization, Windows integration, packaging, updater rollback, and installed-system compatibility have equivalent automated and installation-level coverage.
