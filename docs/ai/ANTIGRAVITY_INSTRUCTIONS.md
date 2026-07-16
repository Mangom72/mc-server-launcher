# Instructions for Antigravity

This document outlines the synchronization and workflow guidelines for Antigravity (the IDE agent) when alternating tasks with Codex. These rules are also reflected in `.agents/AGENTS.md` to be applied automatically to the workspace.

## 1. On Session Start (Context Loading)
- The very first action must be reading `docs/ai/SYNC_STATE.md` and `docs/ai/CODEX_HANDOFF.md` to understand Codex's last actions and pending issues.
- Check the git status (`git status`) for any uncommitted code.

## 2. Design and Implementation
- **Maintain Architecture**: Write code that conforms to the existing modular structure (UI, Network, Bridge, Storage, etc.).
- **UI/UX Standards**: Maintain a strict modern, rounded design (Toss style) for this WinForms app.
- **Verification**: After modifying code, MUST run local builds (`build.ps1`) and tests (`test.ps1`) to check for regressions.

## 3. On Session End (Handoff to Codex)
- Update `docs/ai/SYNC_STATE.md` for Codex.
- **Details to record**:
  1. List of implemented/modified features.
  2. Major architectural or structural changes (things Codex must know).
  3. Unresolved issues and subsequent tasks requested of Codex.
