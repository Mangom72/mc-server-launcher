# AI Agent Synchronization State

## Codex Security Remediation - 2026-07-18

- **Current Version**: 1.5.20 (build 26.2.45.60)
- **Branch**: `codex/release-v1.5.20`
- **Status**: 감사 수정본을 정식 패치 릴리스로 게시하기 위한 버전·변경 기록 및 태그 준비
- UPnP 매핑을 실행별로 기록하고 현재 내부 IP·포트·프로토콜·설명이 정확히 일치할 때만 삭제합니다. 살아 있는 다른 MineHarbor 프로세스의 기록은 보존합니다.
- TCP/UDP 부분 성공, 외부 검사 실패, 중복 재검사, CGNAT dual-stack 비교, 가상 어댑터와 방화벽 규칙 판정을 보수적으로 처리합니다.
- Forge/Inno Setup 다운로드, 백업 복원, 직접 콘솔 위험 명령, 로컬 코드 서명 경로를 강화했습니다.
- 기준 검증: `build.ps1 -SkipDependencyDownload` 성공, `test.ps1 -LauncherPath artifacts\MineHarbor.exe`에서 `PASSED=21`, `BRIDGE_PROTOCOL_PASSED=8`, 버전·smoke·modern-dialog scan 통과.
- 실제 공유기 COM 무응답을 프로세스 내부에서 강제로 중단하는 것은 안전하지 않으므로 호출자는 제한 시간 후 복구 기록을 남기며, 제조사별 장치 시험은 별도 격리 환경에서 수행해야 합니다.

## Current Session Status
- **Agent Name**: Antigravity
- **Last Updated**: 2026-07-16T07:18:00Z
- **Current Version**: 1.5.17 (build 26.2.45.57)
- **Status**: Stable / Released

## Recent Accomplishments
1. **DWM Title Bar Cohesion (Windows 11)**:
   - Integrated TitleBarDwm using dwmapi.dll (DWMWA_CAPTION_COLOR, DWMWA_TEXT_COLOR, DWMWA_BORDER_COLOR) to seamlessly blend the native OS title bar with the application's internal dark/light theme on Windows 11 (Build >= 22000).
   - Preserved FormBorderStyle.Sizable, maintaining 100% of native Windows features like Snap Layouts, multi-monitor DPI scaling, and drop shadows.
   - Handled graceful fallback to default OS title bar for Windows 10 without throwing exceptions.
2. **Handle Recreation Hotfix (v1.5.17)**:
   - Fixed an issue where the C# Form.OnHandleCreated method could not be overridden properly due to internal visibility modifier constraints.
   - Switched to subscribing to the HandleCreated event inside the form constructors (LauncherForm and ServerSetupForm).
   - Ensures DWM color states are preserved even when the window handle is forcefully recreated by the WinForms engine (e.g. ShowInTaskbar updates).
3. **Release v1.5.17**:
   - Bumped version to 1.5.17.
   - Updated CHANGELOG.md with multi-language hotfix notes.
   - Successfully ran Publish-LocalRelease.ps1 and pushed the new artifacts to GitHub Releases.

## Active Issues / Next Steps
- **Monitoring**: Everything is currently stable. Monitor for any reports of unhandled exceptions from dwmapi.dll calls on unusual Windows versions.

## Workspace Context
- **Encoding Rule**: All PowerShell .ps1, C# .cs, and .md files must be saved with **UTF-8 with BOM** to avoid build failures with the local csc.exe and signtool.
- **Certificates**: Local signing throws warnings in PS but still signs successfully.
