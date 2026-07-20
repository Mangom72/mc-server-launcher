# CODEX_HANDOFF.md

## 0. Current implementation state (v1.6.0)

- Active development branch: `codex/content-scheduler-dashboard-v1.6.0`
- Version source of truth: `version.json` = 1.6.0 / build 26.2.45.64
- New per-server state: `.mineharbor/content-manifest.json` and `.mineharbor/automation.json`
- New user areas: installed content/Modrinth/data packs, backup schedules and commands, and a live server status dashboard
- New service boundaries: `ContentManagementServices.cs`, `ServerAutomation.cs`, and the UI integration in `ContentManagementUi.cs` / `ServerManagementFeatures.cs`
- CI: `.github/workflows/ci.yml` validates PRs and main separately from `.github/workflows/build-release.yml`; both build the SDK-style `net48` project and the legacy Portable path. PR #8 run `29729415761` passed both paths.
- Local release validation passed with 24 test groups, 10 bridge protocol cases, Portable smoke/version tests, release artifact/hash verification, UI scan, and security regression scan.
- The default runtime remains .NET Framework 4.8. `MineHarbor.csproj` is the migration bridge; do not switch to .NET 10 until updater, COM/UPnP, WinForms, installer, and Portable compatibility tests are equivalent.
- Do not infer dashboard values. Paper/Purpur TPS/MSPT is shown only when the local bridge reports it; unsupported or disconnected values remain explicit.

This document was created for handoff purposes by analyzing the previous AI (Codex) chat history (`CODEX_CHAT_HISTORY.md`) and the current state of the project.

## 1. Ultimate Goal of the Program
**MineHarbor (Minecraft Server Launcher)**: A modern GUI server launcher for Windows that helps users easily create various types of Minecraft servers (Paper, Purpur, Vanilla, Fabric, Forge, NeoForge) without complex configuration. It automatically manages Java runtimes and server files, assists with external access via UPnP/port-forwarding, and provides multi-server management, Modrinth plugin/mod installation, backups, and command auto-completion.

## 2. User Confirmed Requirements
- **UI/UX**: Modern and rounded design inspired by the "Toss" app. Remove unnecessary alert dialogs, use responsive layouts, and support dark/light themes. Keep button texts concise and use hover tooltips for detailed descriptions.
- **Server Environment**: Automatically download Java runtimes and server files (no offline caching embedded in the EXE).
- **Auto-Update**: Support auto-updating for both the launcher itself (via `update.json` and GitHub Releases) and server files. Removed the minimum 1MB padding restriction for the launcher file.
- **Port Forwarding**: Attempt automatic UPnP mapping if port forwarding fails. Monitor connection status and provide manual setup guides upon failure. Delete ONLY the ports opened by the launcher when closing.
- **Multi-Server Management**: Create, duplicate, delete (with a 30-day trash bin and permanent deletion), archive, and set a default server.
- **Command Bridge**: Communicate with Paper/Purpur servers (127.0.0.1) to provide console and plugin command auto-completion. Apply safety prompts for dangerous commands like `stop`.
- **Localization**: Support Korean and English.
- **No Personal Hardcoding**: Removed hardcoded OP / log-hiding features tied to a specific username (`Mangom72`). Replaced with a generalized "Server Owner" OP system. Repository and executable names are unified to `MineHarbor`.

## 3. Implemented Features So Far
- Single Portable EXE execution and Inno Setup-based installer support.
- Multi-server/profile management with isolated data per server.
- Automatic downloading and caching of server files and Java.
- UPnP automatic mapping, port monitoring, and external IP copying.
- Server Start/Safe Stop, console viewer, and filtering (e.g., coloring harmless compatibility warnings in blue).
- Command auto-completion (Paper/Purpur bridge integration and custom command UI).
- Player management (OP, Whitelist, Kick, Ban).
- Plugin/Mod search and download (via Modrinth API).
- Full backup and 30-day trash bin logic.
- CI/CD release pipeline via GitHub Actions (Automated build, test, and SHA-256 validation).

## 4. Features In Progress or Incomplete
- **Fabric/Forge Bridge**: External bridge command auto-completion for non-Paper servers (like Fabric) is not yet implemented.
- **Authenticode (Code Signing)**: Currently lacks a Windows signing certificate, so running the app may trigger a `NotSigned` SmartScreen warning.
- **High DPI Scaling**: Needs further testing and optimization for edge cases like 125%, 150%+ multi-monitor environments.
- **Modern .NET Runtime**: The repository now has an SDK-style `net48` project, but moving the shipped runtime to .NET 10 remains a staged migration rather than a completed target switch.
- **Metrics Bridge Coverage**: TPS/MSPT metrics are implemented for the Paper/Purpur bridge. Fabric/Forge and Vanilla expose these values as unsupported instead of estimates.

## 5. User Rejected or Discarded Ideas
- **Hardcoded Username Features**: The exclusive auto-OP and log-hiding features for a specific user were rejected for security and versatility.
- **Embedded Server JAR**: Initially planned to embed the server JAR inside the EXE for faster first execution, but rejected due to massive file size inflation. Opted for online downloads with hash verification.
- **1MB Minimum Padding**: The forced dummy data to meet older launcher update mechanisms was used once as a hotfix and then discarded.
- **Long Button Texts**: Rejected designs where long text caused truncation or ellipses (`...`). Replaced with short words and tooltips.

## 6. Important Design Decisions & Rationale
- **`version.json` as SSOT**: `productVersion` and `buildNumber` are managed centrally in `version.json` for both the app and GitHub Actions.
- **Separated UI and Background Logic**: Modularized UPnP, bridge communication, and server management logic into separate classes to reduce complexity.
- **Bridge Communication Security**: Uses a local (127.0.0.1) connection with randomized session token validation to ensure security.
- **STA Thread for UPnP**: UPnP utilizes Windows COM objects, so a Single-Threaded Apartment (STA) model was strictly applied to prevent crashes.
- **Logical Deletion (Trash Bin)**: To prevent accidental data loss, deleted servers are moved to a `servers-trash` directory and preserved for 30 days.

## 7. Discovered Errors and Resolutions
- **Old Paper Versions Crashing Early**: The `--nogui` flag caused errors on older versions. Resolved by applying `--nogui` only to Vanilla/Fabric.
- **Misunderstood Compatibility Warnings**: `sun.misc.Unsafe` warnings are harmless, so they are displayed in blue text to avoid panic.
- **Old JAR Path Errors in Java 11 (Korean Paths)**: Changed execution arguments from absolute paths to relative paths.
- **UI Button Activation Issues**: Fixed a bug where player management buttons remained disabled after the server started.
- **Horizontal Scrolling Issues**: Replaced static coordinates with a responsive `TableLayoutPanel` and dynamic scrollbar UI to adapt to Dark/Light themes properly.

## 8. Current Role per File
- `ModernLauncherGui.cs`: Main window, UI state, and main event loop.
- `ManagedServerDashboard.cs`: Dashboard for switching and managing multiple servers.
- `QuickCommandUi.cs` / `QuickCommandPickerUi.cs`: UI for console commands and the auto-completion popup.
- `QuickCommandsAndBridge.cs`: Communication protocols and bridge handling with Minecraft servers.
- `UpnpExternalAccess.cs`: Handles UPnP communication if port forwarding fails.
- `StorageConfiguration.cs`: Manages user data storage paths (LocalAppData vs. current directory).
- `BackupAndProfileTools.cs`: Manages server profile copying, backups, and restores.
- `ContentAndDiagnostics.cs`: Installation logic for plugins and mods (Modrinth API).
- `ServerTrash.cs`: Manages the 30-day trash bin for server deletion.
- `RuntimeCompatibility.cs`: Java environment checking, downloading, and version management.
- `NetworkAndPlayerTools.cs`: Player permissions management (OP, Ban, Whitelist, etc.).
- `ModernDialogs.cs`: Custom alert/confirmation windows with the modern design.

## 9. Behaviors That MUST Be Maintained
- **UPnP Port Protection**: Only delete ports that were explicitly opened by the launcher. NEVER touch existing user mappings.
- **Data Backup Priority**: Always prompt for a backup or show a warning before executing potentially breaking version changes.
- **`version.json` Dependency**: The CI pipeline relies on this file to package release assets. Its integrity must be preserved.
- **No Personal Identifiers**: No special privileges should be granted to `Mangom72` or any specific user in the logic (except for standard GitHub URLs).

## 10. Next Steps / Tasks to be Done
- Keep the passed PR #8 Windows SDK/legacy build gates required when merging or extending this work.
- Expand bridge command completion and TPS/MSPT metrics to Fabric/Forge if a stable server-side API is selected.
- Prepare for Windows Code Signing (Authenticode) and validate SmartScreen reputation on signed releases.
- Exercise 125%, 150%, and mixed-DPI multi-monitor layouts with representative long Korean and English strings.
- Continue the .NET 10 migration sequence in `docs/architecture/DOTNET_MODERNIZATION.md` without changing the shipped target until compatibility gates pass.

## 11. Conflicts Between History and Actual Code
- **File Name Changes**: Chat history initially mentions `Paper-26.2-Server.exe` or `Minecraft-Server-Launcher.exe`, but the final applied app name is `MineHarbor.exe`. All logic and docs must adhere to this latest name.
- **Admin Features**: `Mangom72Admin` modes and log-hiding features only exist in historical records and have been completely removed from the actual codebase.

## 12. Project Maintenance and Deployment Policies (Confirmed from Chat History)
- **Version Bump Criteria (`productVersion`)**: Follows Semantic Versioning. Bug fixes bump the patch (`x.x.1`), new features bump the minor (`x.1.x`), and breaking changes bump the major (`1.x.x`) version.
- **Internal Build Number (`buildNumber`)**: When updating `version.json`, increment the very last digit by 1 alongside the `productVersion` (e.g., `26.2.45.36` -> `26.2.45.37`).
- **Deployment Workflow**: Never commit or push directly to `main`. Push a `codex/` feature branch, require the PR CI to pass, merge through review, and only then create the matching `v1.x.x` tag or explicitly dispatch the release workflow. The release workflow rebuilds, tests, packages, verifies hashes, and publishes the final release.
