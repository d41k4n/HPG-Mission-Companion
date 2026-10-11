## Release 0.10

- Tools tab, Check for updates (when the program starts, at most once every 6 hours, option "Check for updates when the program starts" on by default, and on the button): reads the R&E mission file installed in the Community folders (the id HEMSRandE in the head of the file, its title carries the release, for example "0.997 169.45") and the copy of the mission kept in GeneratedMissionIndex.json, then the published mission and the published program. Mission: GitHub release of the mission repository first (tag with the release, asset everywhere_all.json, size and sha256 from the release), the Google Drive link as a fallback (the title and the size are read with an HTTP Range request of the first 4 KB). Program: latest GitHub release of this repository (asset HPGMissionCompanionSetup.exe). Releases compare as two integers (169.9 comes before 169.45; the program 0.10 after 0.9).
- An automatic check only reads and tells: a balloon of the notification area (and the Tools tab log) says what is newer, and a click on it opens the Tools tab and asks the question; nothing is downloaded without it. If something is newer you are asked once. Yes: the mission file is downloaded and checked (size, sha256 when the release gives it, the file ends with its closing brace, its title is the release announced), the installed file is copied to backups\mission (never inside the Community folder: the index tool would list a copy as a mission), replaced through a .part file, and the mission index is updated; then the program: the setup is downloaded (size and sha256), the program closes and the setup (/S /dir=this folder, no shortcuts when this folder was not installed by the setup) installs and starts it again. The mission always comes first; an error stops the sequence and keeps what was done.
- The mission file to replace is chosen from the copies found (the ones with a release number first; an original file without a release number is replaced only after the question says so) or with Browse; the choice is kept (setting MissionFile). Setting MissionUrl is the fallback link.
- Release notes in the update question and in a button: the question of the update is a window with the notes of what is newer (the body of the GitHub release: for the mission the user changelog of the builds since the previous release, for the program the changelog of the versions), scrollable, with "Download and install" and "Not now" (the default answer is Not now). A mission taken from the Google Drive link has no notes and the window says so. The new button "What's new..." of the Tools tab shows the notes of the latest published mission and program also when nothing is newer.
- Command line for checks without the window: --check-updates <report> [--community <dir>] [--index-file <file>] [--mission-url <url>] [--github-mission <api url>] [--github-app <api url>] and --test-replace <installed> --with <file> --backup <dir>.

## Release 0.9

- Local delivery of the program and of the installer `HPGMissionCompanionSetup.exe` (it embeds the program, SimConnect.dll and the pack EN 7; not pushed to GitHub). Needs the mission 0.997 169.45 for the new variables and messages; with an older mission it works as before.
- The service now listens on 127.0.0.1 only (it was 0.0.0.0): the mission connects to localhost, and Windows Firewall no longer asks to open the port at every start (the mission worked without the answer). New tick in the Service tab, "Accept connections from other computers" (setting `ListenAll`, off by default), brings back the old behaviour and the question; changing it restarts the service at once.
- Clean up (work folder) in four groups: Safe to delete (debug and test tables, the only one ticked), R&E configuration files (resets saved games and profiles), User settings (`global.json`, the global variables of all the missions: use it to reset to default or when a corrupted file stops the mission) and Other files. `global.json` is no longer excluded; deleting it warns that every setting goes back to its default, and the zip backup comes first as always. A `global.json` in a subfolder is listed as Other.
- Crew chatter level: the mission publishes `L:HMC_CHAT_MAX` (setting CREW CHATTER of the mission: FULL 6, REDUCED 3, MINIMAL 1) and the program never picks an option number above it. Option 1 is the default wording, 2 and 3 are professional, 4 to 6 colloquial; a futile message has two silent options first and its default as the third, so REDUCED lets a futile line through only at its default and MINIMAL keeps it silent. Without the variable (an older mission) all six options are used.
- Pack EN 7: 13 new messages with six options each: the dispatcher exchange of the briefing (DSP.RQ, DSP.ANS.ACC, INJ, SAR, TRF, ORG), the crew presentation (BRF.INT.PLT, CPL, DOC, CHF, futile), where the crew is left when the police drives it from a far landing spot (APR.PLT.P with the distance in meters as argument, APR.CHF.P) and the doctor's report for a dead patient (GRD.DEAD).
- Pack EN 6: six options for every message (for the technicians six for each sex). Option 1 is the default (the wav text, corrected); 2 and 3 professional variants; 4 to 6 more and more colloquial, so that a future filter can cut the high numbers. Small talk nobody needs (futile messages, attribute `futile="1"`) has two silent options first (`<o silent="1"/>`), the default is option 3 and its conversation is always coherent. Speed callouts and numbers are the same word six times on purpose.
- New small talk groups BAN.Y (yesterday), BAN.M (a match) and BAN.C (company), futile; the chats of the flight CRZ.* are futile too.
- Silent options: the program understands `<o silent="1"/>` (nothing is said, the option numbers stay). Futile groups are coherent whatever the variety setting.
- The dead and the badly hurt do not speak: the mission publishes `L:HMC_CREW_MUTE` (bits as in `L:HMC_CREW_PRESENT`) and the program keeps the lines of those crew members silent (log line "not said").
- The pack is now edited from `tools/variants-EN/*.json` with `tools/build-variants.js` (the XML is the source, `tools/import-pack.js` is historical); `tools/export-xlsx.js` writes the voice messages spreadsheet from the pack. `tools/check.js` checks the six options.
- Earlier in this stage (pack EN 5): the warning prefixes (Be advised, Attention, For information ...) of the spreadsheet alternatives are gone, the speed callouts are only Vtoss and Vy, the lines after a hard touchdown are serious, and new messages without LVAR called by the mission with the say tag (OBJ, BRF, APR, GRD, DTH, HLD, HOS, MAR.NTC, CRZ; sources tools/pack-dialogs-voice2-EN.json and tools/pack-replace-EN.json, now folded into tools/variants-EN).

## Release 0.8

- Local delivery of the program only: the installer of 0.7 is not rebuilt for this release (set NOSETUP=1 for tools\build.cmd to skip it).
- Piper voices list: click a column header (Voice, Region, Quality, Size, Voice type, Status) to sort by it, click again to reverse; the arrow in the header shows the order and the selected voice stays selected. Installed voices come first when sorted by Status.
- Technicians: actors 6 and 7 are now Technician male and Technician female (and the actors 1 and 2 Pilot and Copilot). The technician of a scene is random, picked by the mission (`L:HMC_TECH_SEX`); the pack messages FRA.* and SARA.* have options for both (`<o sex="m">`, `<o sex="f">`) and are spoken by the technician of the scene; the crew lines that greet the technician use `{@TECH}`. Pack EN 4.
- Names: `{PLT}` is always the job word (Pilot, Copilot, Chief, Paramedic, Doc, Technician), `{@PLT}` the name of the shift or the job word. A name box next to each voice (actors 1-7) in the Actors tab; with "Random names for each shift" each role (and each technician, by sex) takes a first name from the static lists of the program (the first names of the patients of the mission, `src/Names.cs`).

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
