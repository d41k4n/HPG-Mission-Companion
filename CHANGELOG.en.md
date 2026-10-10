## Release 0.7

- Installer `HPGMissionCompanionSetup.exe` (built by `tools\build.cmd`, source `setup/Setup.cs`): per-user install in `%LOCALAPPDATA%\Programs\HPG Mission Companion` (or a folder of your choice), no administrator rights, Start menu and desktop shortcuts, entry in Apps and features, `Uninstall.exe`. It embeds the program, the pack EN (installed with the program's own `--install-pack`, which archives an older version) and `SimConnect.dll` when the build machine has the MSFS SDK. An update keeps the settings, Piper, the voices, the audio folder and the backups; the uninstaller asks before removing that data (default no) and removes the program from the EXE.xml of the simulators through the program itself. Switches: `/S`, `/dir=PATH`, `/nodesktop`, `/nostartmenu`, `/nolaunch`, `/uninstall`, `/removedata`.
- Volume: the program follows `L:VOLUME_CREW` (crew voices and sound files) and `L:VOLUME_CHK` (checklist voice), and lowers a voice that is not a radio call by 12 dB (speaker aboard, view outside) or 9 dB (speaker outside, view inside), from `L:HMC_CREW_PRESENT` and `CAMERA STATE`. New Volume tab. The level is applied to each line when its turn comes.
- Pack EN 3: 321 messages. The new message tone (`SYS.NEWMSG`) is no longer in the pack: the tablet plays its own sound, which is never inhibited, so it sounded twice. New dispatcher messages `DSP.VEH.*` (one sentence for the vehicles on scene and on the way) and the five-line briefing `APR.*` for the approach to the scene.
- Placeholders `{list:N}` and `{be:N}` for lists of vehicles (letters A, F, P).
- About tab: author Andrea Daviero and the Discord links.
- A screenshot run no longer needs the program to be closed.

## Release 0.6

- `L:AUDIOINHIBIT` is watched with the changed flag as well: the simulator resets it to 0 at every flight or mission load and the program writes 1 again at the next simulator frame (the read every 5 s stays as a safety net). Without this the mission could start with the voice code of a wav pack while the program played the voices.

## Release 0.5

- Voice app mode: the program reads the LVAR counters of the mission (voice code `APP`, `L:AUDIOINHIBIT` 1) through SimConnect and speaks the matching message of the pack; the mission calls stay as they are. Sounds without voice (ringtones, new message tone, tablet alarm) play as wav files of the audio folder at the master volume.
- Radio rule from the context the mission publishes (`L:HMC_HELI_OFF`, `L:HMC_CREW_PRESENT`, `L:HMC_CTX_VALID`): helicopter off voice, inside and outside radio, outside and outside voice; the dispatcher always on the radio.
- Voice packs tab: pack played from the simulator variables, texts random or coherent, crew names as functions or random for each shift, state line of the bridge. Pack `vcp` attribute, `<pool>` name lists. `<names:random>` follows the choice of the user.
- Pack EN 2: 309 messages, LVAR templates `L:CPL{VCP}5`, the four crew conversations of the missions (boarding, briefing, debriefing after a good or a bad mission, four options each, 19 new messages with reserved LVAR numbers for wav files to be recorded).
- Tag `<defaultactor:N>`: every text without an actor tag is spoken by actor N from now on (the mission checklists use 11).
- Lines that come from the simulator and wait more than 30 s are dropped. `<plain>` drops HTML-like tags from the text. New test mission `tests/voicepack-bridge-test.json`; `--expand` understands `@watch` and `@settings` lines.
## Release 0.4

- Voice packs: XML files in the `voicepacks` folder with the texts of the mission messages, several options each. A mission speaks a message with `<say:KEY>` and passes dynamic parts as arguments separated by `//` (`{0}`, `{1}` in the option text). `<plain>` speaks a free text without interpreting any tag in it. `<pack:NAME:VER>` declares the pack and the version the mission needs; `<names:..>` sets the names of the crew (`{PLT}`, `{CPL}`, `{PAX1}`, `{PAX2}`, `{PAX3}`); `<variety:random|coherent>` chooses how the options are picked (coherent keeps the same option number through the messages of a conversation).
- Pack `EN` 1: 290 messages from the voice messages spreadsheet (244 LVARs of the wav pack) with up to five text options each, options of a similar length where the mission waits a short time after the message; 46 of them are drafts written from wav file names (`draft="1"`). `tools/import-pack.js` made it.
- New tab Voice packs: list, drag and drop area, Browse, Open folder, Reload, Remove. Installing a newer version of a pack moves the old file to `voicepacks\obsolete`; `--install-pack` does the same from the command line. The pack name is the `name` attribute or, when missing, the file name.
- Radio: optional beep type per text, `<beep:double>` or `<radio:chirp>` (none, click, beep, double, chirp).
- Actor 11 "Checklist" (a voice for the checklists).
- Simulator link: besides `L:AUDIOINHIBIT` the program publishes `L:HMC_VOICE_STATUS`, `L:HMC_VOICEPACK_VERSION` and `L:HMC_APP_VERSION`; the connection no longer depends on the audio inhibit option.
- About tab: the script developer guide (also `docs/DEVELOPER_GUIDE.md`). New command line options `--expand`, `--packs-check`, `--install-pack`.
- Standalone test mission `tests/voicepack-say-test.json` with the test pack `tests/voicepack-test-TST.xml`.
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
