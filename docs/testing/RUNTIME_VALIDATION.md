# Runtime validation

Static checks (`tools/check.js`) and the self-tests of the executable never prove simulator behaviour. The simulator tests are standalone HPG test missions in `tests/`; the user also keeps them in `Desktop\claude\tests`.

## Status (2026-10-10, version 0.3)

| Area | Status |
| --- | --- |
| Tags in a `speak` text (actors, voices, speeds, languages, forced voice, angle and square brackets) | confirmed in the simulator with version 0.1 |
| `<stop>` and the STOP button of the test mission | pending |
| `<vol:N>`, volume from a mission variable, `<radio>` effect | pending (done by ear in the app only) |
| Start with the simulator (rewritten entries, minimized, closing with the simulator) | pending |
| Clean up on the real work folder, Update mission index on the real package | pending (tested only on dummy copies) |
| Automatic index update at start (option on by default) | tested on a temporary Community folder, pending on the real package |
| `L:AUDIOINHIBIT` kept at 1 through SimConnect (every 5 s, option on by default) | pending: only the "no simulator" path is tested (status "waiting for the simulator"); connection, read and write need the simulator (see Simulator link below) |
| Simulator detection (only folders with `UserCfg.opt`) | tested on temporary folders; the real machine must list only the installed simulator |

## Voice test mission

`tests/carls-keys-and-voice-test.json` has a button **2 VOICE CREW DIALOG TEST** (and **STOP VOICE**). It needs the app running.

1. About two minutes of speech: part 1 a conversation of Pilota, Copilota, Doctor, HEMS1, HEMS2 (actors 1-5); part 2 Frank, Sarah, Dispatcher, Ground unit, Patient (actors 6-10); part 3 a Windows voice and a Piper voice named in the tag and forced speeds; part 4 Italian lines and square brackets; part 5 the volume tag at 100, 50 and 20 and a volume passed by the mission (`<vol:{0}>`); part 6 the radio effect, at reduced volume and switched off in the middle of a text; part 7 an interrupt.
2. Every actor must have the voice chosen in the app. All lines with the same voice means the tags were removed on the way: read the `[RAW]` lines of the app log, they show the text as it arrived (check that `<vol:40>` arrives with the number filled in).
3. STOP VOICE must stop the speech and the rest of the test; the status line then says STOPPED. Without a voice server the status line says FAILED.

The same file holds a CARLS keypad hold test that belongs to the mission repository, not to this app.

## Simulator link

1. Start the app before the simulator: the Service tab says "SimConnect: waiting for the simulator"; start the simulator (2024 and, separately, 2020): within about 5 s of a loaded flight the state becomes "connected, L:AUDIOINHIBIT set to 1" (or "= 1") and the log has a `SimConnect` line.
2. In the simulator or the mission, change `L:AUDIOINHIBIT` to 0: within 5 s the app writes 1 again and logs "was 0, set to 1".
3. Untick the option: no more writes; the state says "off". Close the simulator: the state goes back to waiting.
4. If `SimConnect.dll` of the 2024 SDK does not connect to the 2020 simulator, say so: the build can take the 2020 one instead.

## Tools

1. Clean up: close the simulator, open Tools, Clean up, check the groups, tick only debug and test, keep the backup. The files of the other groups, `global.json` and the saved profiles must still be there, and `backups\` must hold the zip.
2. Update mission index: press the button with the Community folder of the simulator; the output ends with SUCCESS, the index file time changes; in the simulator press Refresh in the mission app and a newly added mission file appears.
3. Start with the simulator: tick the option, start the simulator: the app appears in the notification area (not in the taskbar), serves port 5997, and exits about 15 s after the simulator closes.
