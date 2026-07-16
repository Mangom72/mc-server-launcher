import sys

with open('CHANGELOG.md', 'r', encoding='utf-8-sig') as f:
    content = f.read()

new_changelog = '''## [1.5.18] - 2026-07-16

### Korean
- **UI 및 동작 안정화**: 라이트 테마 전환 시 윈도우 타이틀 바 색상이 즉시 동기화되지 않던 문제를 수정했습니다.
- **UPnP 포트포워딩 롤백**: 최근 도입된 소켓 방식 UPnP의 구현 누락으로 포트포워딩이 정상 작동하지 않던 문제를 수정하기 위해 기존의 안정적인 Windows COM(NATUPnP) 방식으로 롤백했습니다.
- **버튼 정렬 수정**: 상단 우측의 런처 업데이트 버튼이 다른 버튼들보다 살짝 높게 위치하던 문제를 수정했습니다.

### English
- **UI & Stability Improvements**: Fixed an issue where the Windows title bar color would not immediately synchronize when switching to the light theme.
- **UPnP Port Forwarding Rollback**: Reverted the UPnP implementation back to the stable Windows COM (NATUPnP) method to fix port forwarding failures caused by incomplete socket-based UPnP logic.
- **Button Alignment Fix**: Fixed a minor layout issue where the launcher update button was positioned slightly higher than adjacent buttons.

'''

content = content.replace('## [1.5.17]', new_changelog + '## [1.5.17]')

with open('CHANGELOG.md', 'w', encoding='utf-8-sig') as f:
    f.write(content)
print("CHANGELOG.md updated with UTF-8 BOM.")