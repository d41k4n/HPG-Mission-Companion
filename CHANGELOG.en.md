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
