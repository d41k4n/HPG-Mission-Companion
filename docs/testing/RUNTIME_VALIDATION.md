# Runtime validation

Static checks (`tools/check.js`) and the self-tests of the executable never prove simulator behaviour. The simulator tests are standalone HPG test missions in `tests/`; the user also keeps them in `Desktop\claude\tests`.

## Check for updates at every start (0.21, runtime PENDING)

With the program 0.21 installed and a newer release published: close the program and start it again at once, twice in a row: both times the log has the lines of the check (`[INFO] Updates: ...`) and, with something newer, the small message in the corner and the amber button. Without internet the log says that the published program could not be read and no Google Drive address is tried; the button Check for updates still falls back to the Drive link for the mission.

## Restart and uninstall without a shell (0.20, tested on dummy folders: --after-pid wait, silent install and uninstall with and without /removedata; runtime PENDING)

The restart of the program (after Set up in one click, Load settings) starts `HPGMissionCompanion.exe --after-pid <pid>`: the new copy waits up to 20 s for the old process to end (its mutex and the port are free) and then starts as usual; no `cmd`, no `ping`. Test: press Set up in one click with everything installed ("Nothing to download"), close the window: the program is gone for a moment and comes back, one icon in the notification area, the log of the new run starts at once and the port is not reported busy. Start `HPGMissionCompanion.exe --after-pid 999999` (a pid that does not exist): it starts without waiting.

Uninstall: `Uninstall.exe` (or Windows Apps, or `/uninstall /S`) copies itself to `%TEMP%\HPGMissionCompanionUninstall-<pid>.exe` and ends; the copy waits for it, closes the program, removes the files, the entries and the folder (only when empty), asks the questions of before, and shows the final message. Test on a dummy folder: `HPGMissionCompanionSetup.exe /S /dir=D:\x /nolaunch` then `D:\x\Uninstall.exe /uninstall /S /dir=D:\x`: after a few seconds the folder is gone, the copy in the temp folder stays until the next setup removes it. A file of yours in the folder stays and the log (`%TEMP%\HPGMissionCompanionSetup.log`) says the folder is not empty. The setup that replaces a running program (the update of the Updates tab) ends it with Process.Kill (and the Piper processes of the folder), never with taskkill.

## Settings profiles, setup in one click, installer (0.19, runtime PENDING)

Window, Service tab, group Settings profile. Save settings to a file: the file dialog opens in `profiles`, the file has the voices, speeds, radio and levels and no folder, device or port; with crew names typed the question asks whether to include them. Load settings from a file: a copy of HPGMissionCompanion.json also works; a file that is not a profile gives a clear message; the question lists what is downloaded and what stays on the default voice. Set up in one click on a PC without Piper: the question says about 530 MB, the window shows the engine and the six voices one after the other with the bar, the settings of before are in `backups\settings`, the program restarts, the Actors and voices tab shows the recommended voices, the Windows voice Mark is replaced by the default if it is not installed (the log says so). With everything installed: "Nothing to download", the settings change and the program restarts. A download that fails (disconnect the network): the window says so, the voices that came are kept, the others use the default, nothing stays half applied. The entry of the simulators follows "start with the simulator" (EXE.xml). Command line: `HPGMissionCompanion.exe --oneclick report.txt --no-download` on a copy of the folder applies the profile without downloading (exit code 0).

Installer (a new folder, no settings): the complete setup box is ticked, the install shows the lines of the program (engine, voices, applied settings); with `/S /oneclick` the same without the window; with `/S` alone nothing of the profile happens. An existing install (settings there): the box is not ticked and says what it replaces; ticking it keeps a copy in `backups\settings`. Closing the installer window during the download is not possible: the setup can take several minutes.

## Decision on a new dispatch (pack EN 12 and mission sources after 169.49, runtime PENDING)

Needs the pack EN 12 installed (pack update of the program) and a mission built from the sources with `voice dispatch triage` (not built yet). First dispatch of the shift: the dispatcher reads it, the copilot asks the crew (DSA.ASK), DSA.GO, the question to the pilot (DSA.ASKP); nothing else is said until the dispatch is accepted on the tablet; then the pilot asks the dispatcher, the dispatcher answers, the crew briefing (PLT 49 and following). Second dispatch (after a mission, helicopter available): the same plus the fuel (DSA.FUEL.OK above the LOWFUELTHRSLD option, DSA.FUEL.LOW at or below it, then DSA.ASKP.B), DSA.MIS with two missions done, DSA.EQP, DSA.HLT. A minute after the question without an answer: DSA.NUDGE once. A newer dispatch while the old one is not accepted: DSA.REASSIGN, then the reading of the new one and a new decision. Accepting a second dispatch reloads the mission: the briefing must start after "Cabin preparation completed!" (log: the first [PLAY] after the reload is the pilot request to the dispatcher). A wav pack and the custom missions: no change. Check the log for the order of the lines (the lines are 1.5 s apart, the program queues them) and that the crew chatter follows the CREW CHATTER and AUDIO_DECLUT options.

## Audio output: devices and levels (0.17, runtime PENDING)

Offline: `HPGMissionCompanion.exe --audio-devices report.txt` lists the output devices and plays 0.3 s of silence on the default device and on each one (every line must say ok); the unknown name resolves to -1 (default) with a log warning. Window: Volume tab with "Show advanced options": the group "Audio output (advanced)" has two device lists (first line Windows default device) and three level sliders; Test buttons. In the simulator: with the default settings nothing changes (default device, 100); choose another device for the headset: intercom, radio calls, the dispatcher and the notifications come out of it, voices of technicians/ground unit/patient stay on the external device; the notification level moves the ringtones, the new message tone and the tablet alarm only; a level multiplies L:VOLUME_CREW and the view attenuation; an unplugged device falls back to the default (the log says so); a STOP/interrupt ends a sound on a chosen device at once.

## Log file, ready announcement and pack update (0.14, runtime PENDING)

1. Log: start the program, a file logs\hmc-<date>-<time>.log appears next to the exe with the first line [INFO] ... started; with the simulator and a mission running the file holds [CTX] lines (battery master, crew aboard mask, context valid) and the [VOICE] lines say intercom / radio / external radio for each line; a start later makes a new file, only the ten newest stay; Open log folder opens the folder.
2. Ready: start the simulator, then the program with `--minimized` (or let the simulator start it): about 3 s after the start the pilot says SYS.READY, at the master volume; with the option off, or started with the window, nothing is said.
3. Pack update: with a release tagged `voicepack-EN` whose EN.xml has a higher version than the installed one, Check for updates shows the pack in the question and the button says Update: pack; installing replaces voicepacks\EN.xml and leaves the old one in voicepacks\obsolete; with the same version, or a minapp above the program, nothing is offered; with no such release the check says nothing about the pack. Dummy test: serve the release JSON from a local file is not possible (the URL is fixed), so test with the real release.

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
