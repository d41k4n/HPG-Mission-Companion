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
| debug | `table_R&E_debug_*` | yes |
| test | `table_R&E_osm_*` and `table_R&E_*_test*` | yes |
| saved | saved games, custom settings and profiles, enabled accidents, aircraft profile links, custom mission | no |
| mission | other `table_R&E_*` (custom hospitals, DF stations, ...) | no |
| other-tables | other `table_*` | no |
| flight | `Flight_*`, `PersistedAircraftState` | no |
| other | unrecognised files | no |

`global.json` is excluded from every group and never touched. Deleting asks for confirmation (with a warning when the saved group is ticked), makes a zip backup first by default (`backups\work-backup-<time>.zip`, relative names kept) and aborts without deleting when the backup fails. A warning appears when the simulator is running.

## Mission index

The HPG tool `hpg-airbus-h145\Tools\Update Mission Index.cmd` (in the Community folders of the simulators) scans the `hpgmission` folders of all packages and writes `html_ui\HPGH145-System\GeneratedMissionIndex.json`. The app runs it with `cmd /c "<tool>" < nul`, so its final `pause` returns at once, and shows its output. It runs when the user presses the button and, while the option "Update the index automatically when the app starts" (on by default, setting `AutoIndex`) is ticked, once when the app starts. Afterwards the mission app of the simulator needs a Refresh.

## Simulator link: L:AUDIOINHIBIT

`src/SimLink.cs` connects to the simulator through SimConnect (native `SimConnect.dll`, P/Invoke, no managed wrapper) and keeps the local variable `L:AUDIOINHIBIT` at 1. A worker thread tries to open the connection every 5 s while the simulator is not running; when connected it requests the variable once every 5 s (`SIMCONNECT_PERIOD_ONCE`, user object, unit `number`) and, if the value is not 1, writes 1 with `SimConnect_SetDataOnSimObject`. L:AUDIOINHIBIT is also watched with the CHANGED flag (the simulator resets it to 0 when a flight or a mission is loaded): a value other than 1 is rewritten at the next simulator frame, the 5 s read stays as a safety net. A SimConnect quit message, a failed request or 30 s without an answer close the connection and the loop starts again. The option "Auto set default audio inhibit L:AUDIOINHIBIT" (setting `AutoAudioInhibit`, on by default) switches it off; the Service tab shows the state. The same connection publishes `L:HMC_VOICE_STATUS`, `L:HMC_VOICEPACK_VERSION` and `L:HMC_APP_VERSION` (see VOICEPACKS.md); the connection no longer depends on the option, which only switches the `L:AUDIOINHIBIT` handling. These four are the only variables the app writes; do not add other writes without the user's request.

`SimConnect.dll` is not in the repository. The program loads it from its own folder (`tools\build.cmd` copies it from `C:\MSFS 2024 SDK\SimConnect SDK\lib`, else `C:\MSFS SDK\...`), then from `MSFS2024_SDK`, `MSFS_SDK`, `C:\MSFS 2024 SDK`, `C:\MSFS SDK`. Without it the Service tab says so and the variable is not set. `--simlink-check <report>` writes the dll found and the state after one connection attempt.

## Start with the simulator

The simulator starts the programs listed in its `EXE.xml`. The app adds, rewrites or removes one `Launch.Addon` named "HPG Mission Companion" (the older name "HPG Voice Hub" is recognised and replaced) in each of:

- `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\EXE.xml`
- `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\EXE.xml`
- `%APPDATA%\Microsoft Flight Simulator\EXE.xml`
- `%APPDATA%\Microsoft Flight Simulator 2024\EXE.xml`

that belong to an installed simulator: the folder must exist and contain `UserCfg.opt`, which the simulator writes when it has run (a folder alone can be left over from an uninstalled or never used simulator). Other entries are kept; the first time a file is changed a copy is saved as `EXE.xml.hpgvoicehub.bak`. The command line is `--minimized` (default on) and `--exit-with-sim` (default on): the app starts hidden in the notification area and leaves 15 s after the simulator process (`FlightSimulator.exe` or `FlightSimulator2024.exe`) has gone. `--autostart on|off` does the same from the command line.
