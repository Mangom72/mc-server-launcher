import re

with open('scripts/Publish-LocalRelease.ps1', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix the broken ? character
content = content.replace('MineHarbor ? Minecraft Server Launcher', 'MineHarbor - Minecraft Server Launcher')
content = content.replace('MineHarbor  Minecraft Server Launcher', 'MineHarbor - Minecraft Server Launcher')

with open('scripts/Publish-LocalRelease.ps1', 'w', encoding='utf-8') as f:
    f.write(content)
print("Publish-LocalRelease.ps1 title fixed.")