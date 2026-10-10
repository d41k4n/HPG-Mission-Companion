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

The HPG tool `hpg-airbus-h145\Tools\Update Mission Index.cmd` (in the Community folders of the simulators) scans the `hpgmission` folders of all packages and writes `html_ui\HPGH145-System\GeneratedMissionIndex.json`. The app runs it with `cmd /c "<tool>" < nul`, so its final `pause` returns at once, and shows its output. It runs only when the user presses the button. Afterwards the mission app of the simulator needs a Refresh.

## Start with the simulator

The simulator starts the programs listed in its `EXE.xml`. The app adds, rewrites or removes one `Launch.Addon` named "HPG Mission Companion" (the older name "HPG Voice Hub" is recognised and replaced) in each of:

- `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\EXE.xml`
- `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\EXE.xml`
- `%APPDATA%\Microsoft Flight Simulator\EXE.xml`
- `%APPDATA%\Microsoft Flight Simulator 2024\EXE.xml`

that exist (folder present). Other entries are kept; the first time a file is changed a copy is saved as `EXE.xml.hpgvoicehub.bak`. The command line is `--minimized` (default on) and `--exit-with-sim` (default on): the app starts hidden in the notification area and leaves 15 s after the simulator process (`FlightSimulator.exe` or `FlightSimulator2024.exe`) has gone. `--autostart on|off` does the same from the command line.
