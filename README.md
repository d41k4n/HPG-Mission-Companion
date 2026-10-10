# HPG Mission Companion

Version 0.3

A companion program for the HPG H145 missions in Microsoft Flight Simulator (2020 and 2024). It runs the voice service of the missions with natural voices, one voice per mission actor, and has tools for the mission work folder and the mission index.

- **Voice service**: the WebSocket voice server the HPG mission system expects (`localhost:5997`), speaking with the voices installed in Windows and with [Piper](https://github.com/rhasspy/piper) voices that the app downloads on request. The text of a `speak` command carries tags that choose the actor, the voice, the speed, the volume, the language and a radio effect. See `docs/architecture/TAGS_AND_PROTOCOL.md`.
- **Actors**: ten actors (Pilota, Copilota, Doctor, HEMS1, HEMS2, Frank, Sarah, Dispatcher, Ground unit, Patient), a voice and a speed for each, per language (English, French, Italian, German).
- **Radio effect**: beep, hiss, band filter and squelch tail, adjustable.
- **Tools**: open and clean the mission work folder (grouped, with a zip backup), run the HPG `Update Mission Index` tool (also at start, option on by default).
- **Simulator link**: keeps `L:AUDIOINHIBIT` at 1 through SimConnect while the simulator runs (option on by default; needs `SimConnect.dll` from the MSFS SDK next to the program).
- **Start with the simulator**: optional, minimized in the notification area, closing with the simulator.

The program is a single Windows executable built with the compiler that ships with Windows (.NET Framework 4, no SDK). The Piper engine and the voices are not part of this repository.

## Build

```
tools\build.cmd            builds bin\HPGMissionCompanion.exe
tools\build.cmd "D:\run"   builds into another folder (for example the runtime folder)
```

Run `node tools/check.js` for the static checks (version agreement, source list, ASCII, no binaries tracked).

## Layout

| Path | Content |
| --- | --- |
| `src/` | C# sources (core and tags, audio effects, engines and player, server, installer, tools, window, program) |
| `assets/` | the icon and the script that draws it |
| `tools/` | build script, static checks, branch guard, release ledger |
| `docs/` | architecture and runtime test procedures |
| `tests/` | standalone HPG test missions used to test the app from the simulator |

Rules for working in this repository: `AGENTS.md`. Third-party software and licences: `THIRD_PARTY.md`.

## Status

Static checks pass; the simulator behaviour (tags arriving through the HPG `speak` command, radio and volume by ear, the real use of the tools) is still being validated. See `docs/testing/RUNTIME_VALIDATION.md`. Anyone may use the program; modifying or redistributing it needs the explicit written consent of the author (see Licence).

## Licence

Use is free for everyone: you may run the program (a build from the author, or built unchanged from this repository) for any purpose, read the source and fork it on GitHub for reading. Modifying the code, copying or redistributing the program or the code, publishing derivative works, sublicensing or selling needs the explicit written consent of the author, as described in LICENSE. Third-party components keep their own licences (THIRD_PARTY.md).
