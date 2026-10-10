# Workspace map

| Path | Role |
| --- | --- |
| `src/Core.cs` | application info and version, settings, tag parser, voice library, voice resolution |
| `src/AudioFx.cs` | wav decoding and encoding, volume, radio effect |
| `src/Engines.cs` | Windows voices, persistent Piper processes, render cache, player queue |
| `src/Server.cs` | WebSocket voice server and message handling |
| `src/Install.cs` | downloads, Piper catalog, voice install and removal, `EXE.xml` start with the simulator |
| `src/Tools.cs` | work folder finding, grouping, backup and deletion, mission index runner |
| `src/SimLink.cs` | SimConnect link: keeps `L:AUDIOINHIBIT` at 1 (read every 5 s, write when different) |
| `src/MainForm.cs` | the window (Service, Actors and voices, Piper voices, Radio effect, Tools, About) and the clean-up dialog |
| `src/Program.cs` | entry point, command line, assembly attributes |
| `assets/` | `app.ico` and `make-icon.ps1`, the script that draws it |
| `tools/` | `build.cmd`, `check.js`, `assert-main-branch.js`, `release-ledger.json` |
| `docs/architecture/` | `TAGS_AND_PROTOCOL.md`, `COMPANION_TOOLS.md` |
| `docs/testing/` | `RUNTIME_VALIDATION.md` |
| `tests/` | standalone HPG test missions |
| `.githooks/`, `.github/` | pre-commit and pre-push guards, the build workflow |

The runtime folder of the app (executable, `HPGMissionCompanion.json`, `piper\`, `voices\`, `audio\`, `backups\`) is outside the repository and holds user data.
