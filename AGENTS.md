# HPG Mission Companion repository contract

## Mandatory start gate

1. Run `node tools/assert-main-branch.js`; work only on `main`, with hooksPath `.githooks`. There is a single branch: no integration branch, no PR merge step.
2. Exactly one registered Git worktree may exist: this folder. Any second worktree, clone, parallel checkout or alternate copy of the sources is a blocking violation. The runtime folder of the app (for example `Desktop\HPGMissionCompanion`) holds only built outputs and user data, never sources.
3. Read `docs/WORKSPACE_MAP.md`. Inspect `git status --short`, preserve user changes and name the semantic scope before editing.

## Source and safety

- Edit `src/`; never edit a built executable. `tools/build.cmd` is the only way to produce `HPGMissionCompanion.exe`.
- Never commit binaries or user data: executables, dlls, wav, onnx, zips, `piper/`, `voices/`, `backups/`, `audio/`, `HPGMissionCompanion.json`, logs. `tools/check.js` fails on them. The Piper engine and the voices are downloaded by the user from their own sources and are never redistributed here (`THIRD_PARTY.md`).
- User-facing strings and source files are ASCII-only unless explicitly requested.
- The text tags and the WebSocket protocol are a contract with the HPG missions (`docs/architecture/TAGS_AND_PROTOCOL.md`): keep them backwards compatible and change the document, the tests and the changelog in the same change.
- Destructive or external actions of the app stay behind the user: cleaning the HPG work folder always shows the groups, asks for confirmation and backs up first by default; `global.json` is never touched; the `EXE.xml` of the simulators is edited only by the app's own code, keeping the `.bak` copy; downloads happen only on a user click, from the documented sources, with md5 checks where the source gives them. Test every destructive path on a dummy copy, never on the user's real folders.
- The index update runs only on the user's click; never add an automatic or start-up update unless the user asks for that option.
- Network, process and file code must not leave processes behind: `piper.exe` children are stopped on exit.

## Build, release, and publication gates

1. Build or generate an artifact only after explicit authorization in the current request. Every authorized release advances the version exactly once (0.1, 0.2, 0.3, ...); `AppInfo.Version` in `src/Core.cs`, the two assembly attributes in `src/Program.cs`, the top entry of `CHANGELOG.en.md`, the "Version" line of `README.md` and `tools/release-ledger.json` must agree (`tools/check.js` enforces the first four against the sources and that the ledger is not ahead). `tools/release-ledger.json` is authoritative for the last delivered artifact; if it disagrees with the changelog or the task history, stop and ask instead of inferring.
2. After an authorized build run `node tools/check.js`, the self-tests of the executable that apply (`--selftest`, `--scan-work`, `--clean-work` on dummy data, `--run-index` on a temporary Community folder), `git diff --check`, status and diff review.
3. A delivered artifact is always named `HPGMissionCompanion.exe`. Static PASS never proves simulator behaviour: runtime status stays pending until tested in the simulator; runtime tests are standalone HPG test missions in `tests/` (and are also handed to the user in `Desktop\claude\tests`), never part of the production mission.
4. Publication is local by default (commit only). Pushing to GitHub requires explicit authorization for that specific push; `main` is the only branch, so a push is directly live. Never push opportunistically.
5. Release completeness is blocking: audit every carried requirement against source, tests, changelog and runtime evidence. Omitted, partial or unverified work blocks delivery; defer only with explicit user consent.

## Documentation

- Durable architecture belongs in `docs/architecture/`; runtime checks in `docs/testing/`.
- Update `CHANGELOG.en.md` only for an externally visible release (one entry per version, newest first).
- Document the current contract, ownership boundary or test procedure; do not write conversational handoffs. Do not claim static checks prove runtime behaviour.
