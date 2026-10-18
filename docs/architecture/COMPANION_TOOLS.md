# Companion tools: work folder, mission index, start with the simulator

## Mission work folder

The HPG mission system writes its tables, saved games and snapshots to the WASM storage of the simulator, in `...\WASM\<MSFS2020|MSFS2024>\hpg-airbus-h145-ap\work` (the H145 mission uses the `-ap` package). The app lists the `hpg-*\work` folders it finds under:

- `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM` (Microsoft Store, 2024)
- `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\WASM` (Microsoft Store, 2020)
- `%APPDATA%\Microsoft Flight Simulator\WASM` and `%APPDATA%\Microsoft Flight Simulator 2024\WASM` (Steam)

and accepts a folder chosen by the user.

"Clean up..." groups the files by name (`Tools.Classify`):

| Group | Files | Ticked by default |
| --- | --- | --- |
| safe (Safe to delete, debug) | `table_R&E_debug_*`, `table_R&E_osm_*` and `table_R&E_*_test*` | yes |
| config (R&E configuration files: resets saved games and profiles) | every other `table_R&E_*`: saved games, custom settings and profiles, enabled accidents, aircraft profile links, custom missions, custom hospitals, DF stations | no |
| settings (User settings: reset to default, or if the mission does not start) | `global.json` in the work folder itself | no |
| other (not of this mission) | other `table_*`, `Flight_*`, `PersistedAircraftState`, unrecognised files, a `global.json` in a subfolder | no |

`global.json` holds the global variables of all the HPG missions and sometimes gets corrupted, which stops the mission: deleting it resets every setting to its default and the mission writes a new one. Deleting asks for confirmation (with a warning when the config or settings group is ticked), makes a zip backup first by default (`backups\work-backup-<time>.zip`, relative names kept) and aborts without deleting when the backup fails. A warning appears when the simulator is running.

## Mission index

The HPG tool `hpg-airbus-h145\Tools\Update Mission Index.cmd` (in the Community folders of the simulators) scans the `hpgmission` folders of all packages and writes `html_ui\HPGH145-System\GeneratedMissionIndex.json`. The app runs it with `cmd /c "<tool>" < nul`, so its final `pause` returns at once, and shows its output. It runs when the user presses the button and, while the option "Update the index automatically when the app starts" (on by default, setting `AutoIndex`) is ticked, once when the app starts. Afterwards the mission app of the simulator needs a Refresh.

## Check for updates (Tools tab)

Target of the mission update: only `hpgmission\RANDOM AND EVERYWHERE\everywhere_all.json` of a package (`MissionCopy.Official`); a package whose folder RANDOM AND EVERYWHERE has no such file gets it created. Other R&E copies (a test copy in `hpgmission`, a file called ORIGINAL) are listed but never chosen by themselves; Browse (setting `MissionFile`) overrides. The backup of the replaced file is `everywhere_all-<release>-<time>.json.bak` in the same folder (a `.bak`, because the index tool lists every `*.json` of the mission folders). The files are copied with a stream copy: the mission folder of the Community is encrypted (EFS) and `File.Copy` of an encrypted file fails with error 6002 where encryption is not possible.

Release notes: the question is the window `NotesForm` (src/NotesForm.cs): the question text above a scrollable read-only box with the notes of what is newer, buttons "Download and install" and "Not now" (default). The notes are the `body` of the GitHub release (`Updates.PlainNotes` turns the Markdown into plain text: capital headings, bullets); the mission release body is the user changelog of the builds since the previous release, written by `tools/user-release-notes.js` of the mission repository, so every release that carries a noteworthy change carries its notes. The button "What's new..." reads the latest release of both repositories and shows the notes also when nothing is newer.

Runs when the program starts (option "Check for updates when the program starts", on by default, the user's request of 2026-10-11; at every start, no time limit, GitHub only: the user's decision of 2026-10-13, the 6 hours were for the Google Drive link, which is not used any more; `LastUpdateCheck` is kept for information) and when the user presses the button. The automatic check only reads: it writes a line in the log and shows a balloon of the notification area when something is newer (a click opens the Tools tab and asks the question); there is no periodic check while the program runs and nothing is downloaded or replaced without the question. Code: `src/Updates.cs`, the dialog in `MainForm.CheckUpdates` and `InstallUpdates`.

**Installed mission.** `Updates.FindInstalled` reads the first 4 KB of every `*.json` of the `hpgmission` folders of the HPG packages in the Community folders and keeps the files whose head has `"id": "HEMSRandE"` (size above 100 KB). The title ("HEMS RANDOM AND EVERYWHERE MISSIONS 0.997 169.45") is the release; a title without the two numbers is an original file (release 0.0). The index (`GeneratedMissionIndex.json` of the HPG package) holds a base64 copy of every mission file: `ReadIndexReleases` decodes the first bytes of each entry and lists the R&E releases, which shows a file replaced without updating the index.

**Published mission.** GitHub only (no Google Drive): `releases/latest` of `davierosoft/HEMS-random-everywhere-mission`, the release in the tag (for example `v0.997-169.45`), the asset `everywhere_all.json` with its size and the sha256 digest of the release. The downloaded file is checked (size, sha256, the head and the closing brace, the release in its title).

**Comparison.** Two integers: 169.9 is before 169.45; the program 0.9 is before 0.10. Family first (0.997).

**Replacement.** Download to the temporary folder, checks (size, sha256 when known, closing brace, title equal to the announced release), copy of the installed file to `backups\mission` of the program folder (never in the Community folder), copy of the new file beside the installed one as `.part`, delete, move. Then the index tool of the user's Tools tab runs. The program: the asset `HPGMissionCompanionSetup.exe` of the latest release of this repository (size and sha256), started with `/S /dir=<this folder>` (and `/nodesktop /nostartmenu` when this folder has no `Uninstall.exe`); the setup closes the running program, so the program saves its settings and exits right after starting it.

Rules: downloads only after the question, mission before program, no write outside the chosen mission file, the backups folder and the temporary folder.

## Simulator link: L:AUDIOINHIBIT

`src/SimLink.cs` connects to the simulator through SimConnect (native `SimConnect.dll`, P/Invoke, no managed wrapper) and keeps the local variable `L:AUDIOINHIBIT` at 1. A worker thread tries to open the connection every 5 s while the simulator is not running; when connected it requests the variable once every 5 s (`SIMCONNECT_PERIOD_ONCE`, user object, unit `number`) and, if the value is not 1, writes 1 with `SimConnect_SetDataOnSimObject`. L:AUDIOINHIBIT is also watched with the CHANGED flag (the simulator resets it to 0 when a flight or a mission is loaded): a value other than 1 is rewritten at the next simulator frame, the 5 s read stays as a safety net. A SimConnect quit message, a failed request or 30 s without an answer close the connection and the loop starts again. The option "Auto set default audio inhibit L:AUDIOINHIBIT" (setting `AutoAudioInhibit`, on by default) switches it off; the Service tab shows the state (in its Advanced box, visible with "Show advanced options" of the window footer, setting `ShowAdvanced`). The same connection publishes `L:HMC_VOICE_STATUS`, `L:HMC_VOICEPACK_VERSION` and `L:HMC_APP_VERSION` (see VOICEPACKS.md); the connection no longer depends on the option, which only switches the `L:AUDIOINHIBIT` handling. These four, and `L:HMC_DATA_REV` (the counter of the shared waypoint table, user request 2026-10-18, see SHARED_DATA.md), are the only variables the app writes; do not add other writes without the user's request.

`SimConnect.dll` is not in the repository. The program loads it from its own folder (`tools\build.cmd` copies it from `C:\MSFS 2024 SDK\SimConnect SDK\lib`, else `C:\MSFS SDK\...`), then from `MSFS2024_SDK`, `MSFS_SDK`, `C:\MSFS 2024 SDK`, `C:\MSFS SDK`. Without it the Service tab says so and the variable is not set. `--simlink-check <report>` writes the dll found and the state after one connection attempt.

## Start with the simulator

The simulator starts the programs listed in its `EXE.xml`. The app adds, rewrites or removes one `Launch.Addon` named "HPG Mission Companion" (the older name "HPG Voice Hub" is recognised and replaced) in each of:

- `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\EXE.xml`
- `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\EXE.xml`
- `%APPDATA%\Microsoft Flight Simulator\EXE.xml`
- `%APPDATA%\Microsoft Flight Simulator 2024\EXE.xml`

that belong to an installed simulator: the folder must exist and contain `UserCfg.opt`, which the simulator writes when it has run (a folder alone can be left over from an uninstalled or never used simulator). Other entries are kept; the first time a file is changed a copy is saved as `EXE.xml.hpgvoicehub.bak`. The command line is `--minimized` (default on) and `--exit-with-sim` (default on): the app starts hidden in the notification area and leaves 15 s after the simulator process (`FlightSimulator.exe` or `FlightSimulator2024.exe`) has gone. `--autostart on|off` does the same from the command line.
