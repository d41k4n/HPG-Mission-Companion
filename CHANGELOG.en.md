## Release 0.3

- New: the program keeps the simulator variable `L:AUDIOINHIBIT` at 1 through SimConnect. While the simulator is connected it reads the variable every 5 seconds and writes 1 when the value is different; without the simulator it retries every 5 seconds. Option "Auto set default audio inhibit L:AUDIOINHIBIT" in the Service tab, on by default; the Service tab shows the SimConnect state. `SimConnect.dll` comes from the MSFS SDK: `tools\build.cmd` copies it next to the program, the program also looks in the SDK folders.
- Service tab: the mission text test and the log are in one box; the log is dark green with a colour per kind of message (voices in yellow); `Exit program` sits bottom right, outside the tabs; next to `Save settings` the red notice "Modified settings, please save" appears when something changed, and leaving with unsaved settings asks Yes / No / Cancel.
- Tools tab: option "Update the index automatically when the app starts" (on by default) next to `Update mission index`.
- Simulator detection: a simulator folder counts only when it holds `UserCfg.opt` (leftover folders of uninstalled simulators are no longer listed or written to).
- Log line "Tools: N work folder(s), M index tool(s) found".
## Release 0.2

- Renamed to HPG Mission Companion: executable `HPGMissionCompanion.exe`, settings `HPGMissionCompanion.json`. Existing start-with-simulator entries named "HPG Voice Hub" are recognised and rewritten when the option is applied again (new command `--autostart on|off`).
- New icon: four rotor blades with motion blur turned 30 degrees, over a double-arrow fuselage (simulator - app - simulator).

## Release 0.1

- Voice service for the HPG missions on port 5997: Windows voices and Piper voices (engine and voices installed from the app), ten actors with a voice and a speed per language, tags `<actorN>`, `<voice name>`, `<speed:x>`, `<EN>` (square brackets accepted).
- Volume tag `<vol:N>` with a master volume, radio effect tag `<radio>` / `<radio:off>` with its own settings page (hiss, start beep, squelch tail, sound), `<stop>` with an interrupt.
- Tools tab: mission work folder (find, open, clean by groups with a zip backup) and the mission index update.
- Start with Microsoft Flight Simulator 2020 and 2024 through `EXE.xml`, minimized by default, closing with the simulator.
- About tab, tray icon, save button, version in the title.
- Runtime validation in the simulator pending.
