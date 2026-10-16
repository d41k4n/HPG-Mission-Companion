# Voice protocol and text tags

The HPG mission system speaks through an external voice server on `localhost:5997` (WebSocket); see https://davux.com/docs/missionsys/TOPIC-SOUND.html. The protocol is one way, from the mission to the server: the mission cannot receive data back through it. The program listens on `127.0.0.1` only, which is enough for `localhost` and keeps Windows Firewall from asking to open the port; the setting `ListenAll` (tick "Accept connections from other computers" in the Service tab) listens on every interface instead.

## Messages

JSON text frames. Field names are case-insensitive, `"interrupt":1` and `true` are the same.

| Message | Effect |
| --- | --- |
| `{"Text":"...","Interrupt":false}` | speak the text (queued after what is playing) |
| `{"Text":"...","Interrupt":true}` | cancel what is playing and queued, then speak |
| `{"FileName":"x.wav"}` | play `audio\x.wav` from the audio folder (volume and radio do not apply) |
| `{"Interrupt":true}` with no text | cancel everything |

Ping and close frames are answered. A text that has only whitespace and an interrupt acts like the last row.

## Tags in `Text`

Tags are written in angle brackets, or in square brackets with the same meaning. They apply to the text that follows them until the same kind of tag appears again; several can be chained and one message can contain several parts, which are spoken in order.

| Tag | Meaning |
| --- | --- |
| `<actor3>` | actor 1 to 11 (11 = Checklist): the voice and the usual speed the user chose for that actor in the chosen language |
| `<Microsoft David>`, `<ryan>`, `<en_US-ryan-medium>`, `<voice:ryan>` | a voice by name (exact name first, then part of a name; the language of the text wins, then the best quality); it forces the voice |
| `<speed:1.2>` (also `<rate:..>`) | speed 0.5 to 3.0; it forces the speed, otherwise the actor's slider is used (1.0 for a forced voice with no actor) |
| `<vol:60>` (also `<volume:..>`) | volume 0 to 100 of the text; the gain is the volume times the master volume of the Volume tab; without the tag 100 |
| `<radio>`, `<radio:on>`, `<radio:off>` | radio call on or off: beep of the user, hiss of the user + 10, band filter, squelch tail; without the tag the voice is clean |
| `<ics>`, `<ics:off>` | the wired intercom of the helicopter: a click, no beep, the hiss of the user, no squelch tail |
| `<radio:ext>` | a call on an external radio (dispatcher, hospital): double beep, hiss of the user + 20, squelch tail |
| `<zone:far>`, `<zone:near>` | a voice heard from inside that comes from 30 to 100 m away: very low (-20 dB); `near` cancels it. The program adds it itself from `L:HMC_ZONE_*` |
| `<beep:double>` | optional beep type of the radio effect for the text: `none`, `click`, `beep`, `double`, `chirp` (or 0-4); without it the type chosen in the Radio effect tab |
| `<radio:chirp>` | shortcut: radio on with that beep type |
| `<defaultactor:11>` | state tag: from now on a text with no actor tag is spoken by that actor (the mission checklists use 11); `<defaultactor:off>` ends it; not reset by a new connection, only by the next tag or a restart |
| `<say:KEY>args`, `<text:KEY>English//value`, `<plain>text`, `<pack:NAME:VER>`, `<names:..>`, `<variety:..>` | voice pack tags, expanded before the tags above (see VOICEPACKS.md) |
| `<dsp:INDEX:CITY>` | who the dispatcher is (state tag, no sound): INDEX is the position of the CARLS group in the static `GRPID` of the mission (-1 the manual frequency), CITY the city of the base (empty or an unresolved `..._Name` when unknown); the texts of the pack say it with `{DSP}` / `{@DSP}` (see VOICEPACKS.md) |
| `<voices:shift>`, `<voices:dispatch>`, `<voices:all>` | a new draw of the voices of the actors set to Random voice (shift: dispatcher; dispatch: ground unit and heli rescuer); no sound |
| `<EN>` `<FR>` `<IT>` `<DE>`, `<language:us>` | language of the user's voice set; without it the "language of the texts" of the Service tab |
| `<stop>` | with `"interrupt":1`, stops what is playing and speaks nothing |

Text with no tag is spoken by actor 1. HTML-like tags (`<br>`, `<b>`, `<i>`, `<u>`, `<p>`, `<em>`, `<strong>`, `<span>`, `<font>`) are ignored. A tag that is not one of the above is read as a voice name; if no such voice is installed the actor's voice speaks and the log says so. If the language has no voice at all, any installed voice speaks.

The volume of a mission variable goes in through the `speak` params, with no SimConnect needed:

```json
{"speak":{"text":"<actor1><vol:{0}><radio>Pilot to dispatch, ready.","params":[{"global":"VOLUME_CREW"}]}}
```

The voice pack tags are resolved first (`Say.Process`): `<say:KEY>` is replaced by the actor tag of the message, the radio tag of the message when the mission sent none and the chosen option with its placeholders filled; everything after `<say>`, `<text>` and `<plain>` is literal text (the characters `< > [ ]` are removed from it). `<text:KEY>English//value//value` is a free text with a key: the active pack's message KEY replaces the English when it has one (its `{0} {1}` take the values), otherwise the English is spoken; the tags before it stay (`Say.ExpandText`).

## Resolution order

1. language: the language tag, else the default language of the Service tab;
2. voice: the named voice if installed, else the voice saved for the actor (actor 1 when none is given), else the n-th installed voice of the language;
3. speed: the speed tag, else the actor's speed (1.0 for a forced voice with no actor);
4. volume and radio: the tags, then the master volume.

## Audio

Windows voices are synthesised through `Windows.Media.SpeechSynthesis` (reflection, no SDK). Piper runs as one persistent `piper.exe` per voice, speaker and speed (at most four), reading text lines and answering with the path of a wav file. Rendered speech is cached in memory (48 MB). The radio effect and the volume are applied to the rendered wav before playback: band filter (Clean 250-4500 Hz, Aviation 350-2800 Hz and compressed, Rough 500-2400 Hz and distorted), hiss at the chosen level, start beep (Click, Beep 1000 Hz, Double beep, Chirp) and a squelch burst at the end; the output peak is limited to 0.98.
