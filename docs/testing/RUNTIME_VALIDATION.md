# Runtime validation

Static checks (`tools/check.js`) and the self-tests of the executable never prove simulator behaviour. The simulator tests are standalone HPG test missions in `tests/`; the user also keeps them in `Desktop\claude\tests`.

## Translated packs and the texts of the mission (0.13, runtime PENDING)

The mission speaks its checklist rows and its dispatch descriptions as `<text:KEY>English` and the program replaces the English with the text of the active pack. Offline (`--expand`, no simulator): install `tests/voicepack-test-TXT.xml` (`--install-pack`), then `--expand report.txt --lines tests/text-keys-lines.txt`. Expected: with `bridge=EN` the English of every line and no UNTRANSLATED line, except the line with the text that is not the one of the key (`A text that is not the one of the key`), which is spoken in English and the log says the text is not the one of the base pack; with `bridge=TXT` the checklist row 02 and the description DSC.1.1 in Italian, the vehicle sentence `Veicolo: Toyota red.` (the test pack puts the values in the other order), rows `CHK.interiorckl.03` and the key `NOPE.1` in English with one `[UNTRANSLATED]` log line for each key (the second `CHK.interiorckl.03` line adds none), and DSC.1.2 (a text the test pack has but with another English in the mission line) in English with the stale-key log line. `--packs-check report.txt`: TXT is level 2 (red) with its missing keys listed, EN is the base pack. `--pack-template out.xml --lang IT --name IT`: a pack with the messages of EN, `basever` = its version, loadable by `--packs-check`. `--pack-add-missing TXT`: TXT gets the missing messages in English, `basever` and version up, the old file in `voicepacks\obsolete`; `--packs-check` then shows level 0 or 1. Window: Voice packs tab, the TXT row is red with the count of the missing texts and the first keys when selected, the line under "Active pack used by the mission" turns red when TXT is the active pack. In the simulator: with a translated pack active, the dispatcher reads the description in the language of the pack and the checklists too; an English text only for the keys that the pack lacks.

## Check for updates (0.10, tested offline on dummy folders, runtime PENDING)

Target and backup (0.11): with the pack of the user (a test copy `hpgmission\everywhere_all.json` and a folder `RANDOM AND EVERYWHERE` without everywhere_all.json) Check for updates names the mission file to create in `RANDOM AND EVERYWHERE`; Yes writes `RANDOM AND EVERYWHERE\everywhere_all.json` and leaves the test copy untouched; a second update keeps `everywhere_all-<release>-<time>.json.bak` beside it. The files of the Community pack are encrypted (EFS): the replacement must not fail with "the specified file could not be encrypted".

1. `--check-updates report.txt --community <dummy Community> --index-file <GeneratedMissionIndex.json> --mission-url <Drive link> --github-mission <api url> --github-app <api url>`: the installed copies (title, release, size), the releases the index lists, the published mission by Range request and by GitHub, the program release. The dummy Community folder holds `hpg-x\hpgmission\<file>.json` with a head `{"title": "HEMS RANDOM AND EVERYWHERE MISSIONS 0.997 169.45", "id": "HEMSRandE"`.
2. `--test-replace <dummy installed file> --with <new file> --backup <dir>`: the backup holds the old file, the installed file is the new one and no `.part` file is left.
3. The notes window: with a published release newer than the installed mission or program, the question shows the notes of each one (scroll to the end), "Not now" is the default button and closes without downloading; the Tools button "What's new..." shows the notes of the latest mission and program releases even when nothing is newer (an offline PC shows the error of each source and not a crash).
4. The window: Tools, Check for updates with an older dummy file: the question names the mission and the program releases; Yes downloads the mission, replaces it, updates the index (tool of the Tools tab) and then starts the setup; the program closes and starts again with the new version. With nothing newer the log says "Nothing to update".
4. Failures to try: no network, a Drive link that is not shared, a release without the asset, a truncated download (the file is refused and the old one stays).

## Status (2026-10-10, version 0.4)

| Area | Status |
| --- | --- |
| Tags in a `speak` text (actors, voices, speeds, languages, forced voice, angle and square brackets) | confirmed in the simulator with version 0.1 |
| `<stop>` and the STOP button of the test mission | pending |
| `<vol:N>`, volume from a mission variable, `<radio>` effect | pending (done by ear in the app only) |
| Test buttons and "Try a text" with `L:VOLUME_CREW` = 0 (simulator running, mission not loaded or slider at 0): the test is audible, a line of the mission is silent | harness on a draft build: the log shows `vol 100 (test: ...)` for the test and `vol 0` for the mission line (silent mode, so not heard); pending by ear in the installed app |
| Radio rules (intercom, radio, external radio, voice, far voice, silence; dispatcher with the battery off or at the base): `HPGMissionCompanion.exe --expand report.txt --lines tests\radio-rules-lines.txt --seed 1` | decisions checked offline on a draft build (`<ics>`, `<radio>`, `<radio:off>`, `<radio:off><zone:far>`, dropped dispatcher text, `fx=ics`/`fx=ext` in the report); the sounds (click, hiss + 10 / + 20, double beep) and the context variables from the mission are pending in the simulator, the mission does not publish `L:HMC_CREW_EXIST`, `L:HMC_AT_BASE`, `L:HMC_ZONE_*` yet (the battery is read from the aircraft variable `L:H145_SDK_OH_BATTERY_MASTER`) |
| Updates tab: the footer button Check for updates opens the tab; the scope Both / Only the mission / Only the program (setting `UpdateWhat`); amber tab, status line and button when something newer is published (also after the automatic check at start) | layout and the amber state checked in screenshots of a draft build (`--screenshot` writes `<prefix>-updates-alert.png`); the check, the question and the install with each scope are pending (need a published release newer than the installed ones) |
| Start with the simulator (rewritten entries, minimized, closing with the simulator) | pending |
| Clean up on the real work folder, Update mission index on the real package | pending (tested only on dummy copies) |
| Automatic index update at start (option on by default) | tested on a temporary Community folder, pending on the real package |
| `L:AUDIOINHIBIT` kept at 1 through SimConnect (every 5 s, option on by default) | pending: only the "no simulator" path is tested (status "waiting for the simulator"); connection, read and write need the simulator (see Simulator link below) |
| Voice packs: `<say>` expansion, arguments, names, plain text, beep types, variety, versions | tested offline (`--expand`, `--packs-check`, the Voice packs tab); pending with the mission (`tests/voicepack-say-test.json`) |
| Voice app mode: counters read through SimConnect, messages spoken, radio rule from the context, sounds without voice, dispatcher reading, conversations, checklist voice 11 with the object form `speak` + `interrupt` | tested offline only (`@watch` lines of `--expand`); pending in the simulator (`tests/voicepack-bridge-test.json`, then the real mission) |
| `L:HMC_VOICE_STATUS`, `L:HMC_VOICEPACK_VERSION`, `L:HMC_APP_VERSION` published through SimConnect | pending in the simulator |
| Drag and drop of a pack onto the Voice packs tab | pending (not clickable from the checks; the code path is the same as Browse) |
| Simulator detection (only folders with `UserCfg.opt`) | tested on temporary folders; the real machine must list only the installed simulator |

## Voice test mission

`tests/carls-keys-and-voice-test.json` has a button **2 VOICE CREW DIALOG TEST** (and **STOP VOICE**). It needs the app running.

1. About two minutes of speech: part 1 a conversation of Pilota, Copilota, Doctor, HEMS1, HEMS2 (actors 1-5); part 2 Frank, Sarah, Dispatcher, Ground unit, Patient (actors 6-10); part 3 a Windows voice and a Piper voice named in the tag and forced speeds; part 4 Italian lines and square brackets; part 5 the volume tag at 100, 50 and 20 and a volume passed by the mission (`<vol:{0}>`); part 6 the radio effect, at reduced volume and switched off in the middle of a text; part 7 an interrupt.
2. Every actor must have the voice chosen in the app. All lines with the same voice means the tags were removed on the way: read the `[RAW]` lines of the app log, they show the text as it arrived (check that `<vol:40>` arrives with the number filled in).
3. STOP VOICE must stop the speech and the rest of the test; the status line then says STOPPED. Without a voice server the status line says FAILED.

The same file holds a CARLS keypad hold test that belongs to the mission repository, not to this app.

## Voice pack test mission

1. Start the program; in the Voice packs tab the pack EN is listed (version 1, 290 messages, OK). Drag `tests\voicepack-test-TST.xml` onto the tab (it also tests the install and the pack name taken from the file name).
2. Load `tests\voicepack-say-test.json` in the simulator and press START VOICEPACK TEST (about 6 minutes). The briefing lists what to listen for. READ STATUS LVARS shows the three variables of the program.
3. The log of the program must show `[RAW]` lines with the tags intact, `[SAY]` lines with the option chosen, and warnings only for the unknown key NOPE.1 and the pack EN version 99.

## Voice app mode test mission

1. Start the program (it sets `L:AUDIOINHIBIT` to 1; the log says "SimConnect: watching 312 variables"), pack EN version 2 in the Voice packs tab. Optional: choose Coherent in the Voice packs tab.
2. Load `tests\voicepack-bridge-test.json` and press START BRIDGE TEST (about 3 minutes). The briefing lists what to listen for.
3. The bridge line of the Voice packs tab shows the context and the last message. If nothing is ever spoken, read the log for SimConnect exceptions: a variable that does not exist yet can make SimConnect refuse the definition.

## Simulator link

1. Start the app before the simulator: the Service tab (box Advanced, shown with "Show advanced options" in the window footer) says "SimConnect: waiting for the simulator"; start the simulator (2024 and, separately, 2020): within about 5 s of a loaded flight the state becomes "connected, L:AUDIOINHIBIT set to 1" (or "= 1") and the log has a `SimConnect` line.
2. In the simulator or the mission, change `L:AUDIOINHIBIT` to 0: within 5 s the app writes 1 again and logs "was 0, set to 1".
3. Untick the option: no more writes; the state says "connected (audio inhibit option off)". Close the simulator: the state goes back to waiting.
4. If `SimConnect.dll` of the 2024 SDK does not connect to the 2020 simulator, say so: the build can take the 2020 one instead.

## Tools

1. Clean up: close the simulator, open Tools, Clean up, check the four groups (Safe to delete, R&E configuration files, User settings with global.json, Other files), tick only the first, keep the backup. The files of the other groups, `global.json` and the saved profiles must still be there, and `backups\` must hold the zip. Then tick only User settings: `global.json` is gone and the mission writes a new one at the next start with every setting at its default (the zip holds the old one).
2. Update mission index: press the button with the Community folder of the simulator; the output ends with SUCCESS, the index file time changes; in the simulator press Refresh in the mission app and a newly added mission file appears.
3. Start with the simulator: tick the option, start the simulator: the app appears in the notification area (not in the taskbar), serves port 5997 on 127.0.0.1, and exits about 15 s after the simulator closes. Windows Firewall must not ask to open the port, and the mission still connects (the log of the program shows the connection); with "Accept connections from other computers" ticked it listens on 0.0.0.0 and the question may come back.
