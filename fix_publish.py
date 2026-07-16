import re

with open('scripts/Publish-LocalRelease.ps1', 'r', encoding='utf-8') as f:
    content = f.read()

old_cmd = '''  if () {
    gh release create   --title  --notes-file  --draft
  } else {
    gh release create   --title  --notes-file  --latest
  }'''

new_cmd = '''  if () {
    gh release create  --title  --notes-file  --draft
  } else {
    gh release create  --title  --notes-file  --latest
  }
  gh release upload  '''

content = content.replace(old_cmd, new_cmd)

with open('scripts/Publish-LocalRelease.ps1', 'w', encoding='utf-8') as f:
    f.write(content)
print("Publish-LocalRelease.ps1 updated.")