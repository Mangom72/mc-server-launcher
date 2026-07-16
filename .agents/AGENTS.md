# AI Agent Common Project Rules (MineHarbor)

These rules are mandatory guidelines that ANY AI agent (including Antigravity and Codex) MUST follow when working in the MineHarbor workspace.

## 1. Handoff & State Synchronization (MANDATORY)
- **On Session Start**: Before making any changes, you MUST read `docs/ai/SYNC_STATE.md` and `docs/ai/CODEX_HANDOFF.md` first. Understand the previous agent's work, pending issues, and uncommitted changes.
- **On Session End**: When finishing a session or passing the context to another agent, you MUST update `docs/ai/SYNC_STATE.md`. Clearly record implemented features, major architectural changes, and the next steps for the incoming agent.

## 2. Project Coding Guidelines
- **Version Policy**: When deploying after fixing bugs or adding features, update `version.json` (apply Semantic Versioning for `productVersion` and increment the last digit of `buildNumber`).
- **Architecture**: Do not break the existing modular architecture (UI, Network, Bridge, Storage, etc.). Follow the established structural patterns.
- **UI/UX Standards**: This is a WinForms-based app, but you MUST strictly maintain a modern, rounded design (Toss app style). Avoid using default Windows alert dialogs; utilize custom dialogs (`ModernDialogs.cs`).
- **Preserve Existing Behaviors**: When shutting down the launcher, only delete UPnP external ports that were explicitly opened by the launcher. NEVER modify ports manually opened by the user.
- **Verification**: After modifying code, you MUST run `build.ps1` and `test.ps1` to ensure local tests pass successfully.

## 3. Release & Upload Workflow (MANDATORY)
When the user requests to release, build, or upload a new version, strictly follow this workflow:
1. **Update Version**: Run `.\scripts\bump-version.ps1` with the appropriate flag (`-Major`, `-Minor`, or `-Patch`) to automatically increment `version.json`.
2. **Update CHANGELOG**: Ensure `CHANGELOG.md` has a new section matching the bumped `productVersion` (e.g., `## [1.5.0]`) outlining the new changes. **MANDATORY:** You MUST include both `### Korean` and `### English` subsections for every release to support multi-language release notes in the launcher.
3. **Publish Local Release**: Run `.\scripts\Publish-LocalRelease.ps1` (It will automatically handle dependency downloads, InnoSetup installation if missing, code signing via `sign-build.ps1`, packaging via `New-ReleaseArtifacts.ps1`, and uploading using `gh release`).
4. **Verification**: Confirm that `gh release list` reflects the newly published version.
5. **Encoding Warning**: When modifying `.ps1` scripts that contain Unicode characters, or when modifying `CHANGELOG.md`, ENSURE the files are saved as **UTF-8 with BOM** (e.g. `[Text.Encoding]::UTF8`). Windows PowerShell 5.1 will misinterpret Unicode strings if the BOM is missing.
