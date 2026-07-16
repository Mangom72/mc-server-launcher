# Instructions for Codex

This document contains the synchronization and workflow guidelines that Codex must follow when alternating tasks with Antigravity (the IDE agent).

## 1. On Session Start (Context Loading)
- ALWAYS read `docs/ai/CODEX_HANDOFF.md` and `docs/ai/SYNC_STATE.md` before taking any action.
- Thoroughly understand the project structure and Antigravity's latest work before modifying the codebase.
- NEVER arbitrarily change the versioning policy defined in `version.json`.

## 2. Precautions When Modifying Code
- **Semantic Versioning**: When releasing changes, update `productVersion` and `buildNumber` in `version.json` according to the established rules (Semantic Versioning for the product, +1 to the last digit for the build).
- **Port Forwarding (UPnP)**: Upon launcher exit, delete ONLY the ports opened by the launcher. Do not delete existing user mappings.
- **UI Consistency**: Avoid default Windows alert dialogs. Maintain the modern UI/rounded button design (Toss style). Use tooltips instead of excessively long button names.

## 3. On Session End (Handoff to Antigravity)
- When finishing your work or preparing a handoff to Antigravity, you MUST update `docs/ai/SYNC_STATE.md`.
- **Details to record**:
  1. Summary of completed work.
  2. Any ongoing errors or incomplete features.
  3. Concrete "Next Steps" for Antigravity.
  4. Current branch state and whether changes have been committed.
