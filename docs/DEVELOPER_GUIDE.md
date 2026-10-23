HPG MISSION COMPANION - SCRIPT DEVELOPER GUIDE
==============================================

Everything a mission author needs to make an HPG mission talk through this program. The same text is in the About tab of the program.

1. HOW A MISSION TALKS TO THE PROGRAM
-------------------------------------
The program is the "voice server" of the HPG mission system: a WebSocket server on localhost, port 5997. The port is fixed (the mission system connects to 5997 and nowhere else, so there is no option; an old settings file with another port is set back to 5997 and the log says it; `--port` is for tests only). The traffic is one way, from the mission to the program. The mission connects once and then uses the speak command:

  {"connect_voice_server":{"on_connected":[...],"on_disconnected":[...]}}
  {"speak":"Hello"}                                          plain text
  {"speak":{"text":"Hello {0}","params":[{"local":"NAME"}]}}  text with parameters ({0} {1} ...), filled by HPG before sending
  {"speak":"Stop now","interrupt":1}                         cut the speech and play this
  {"speak":"file.wav","is_audio_file":1}                     play file.wav from the audio folder (Service tab)
  {"fn":"is_voice_server_connected"}                         query: 1 when connected

On the wire every speak is {"Text":"...","FileName":"","Interrupt":false}. The program answers nothing: the mission cannot know when a speech ends. speak returns at once.

What interrupt does: with interrupt 1 the program first throws away every speech still waiting in its queue and stops the sound that is playing at this moment, then speaks the new text. Without interrupt the new text goes at the end of the queue and is spoken when the earlier ones have finished, in order. A speak with an empty text and interrupt 1 only stops everything (the tag <stop> does the same). It cannot cut a text that has not arrived yet.

2. TAGS IN THE TEXT
-------------------
Tags are written in the text, in angle brackets or in square brackets (<actor3> and [actor3] are the same). A tag applies to the text that follows it, up to the next tag of the same kind, inside the same speak. Every speak starts clean: actor 1, the default language, no radio.

  <actor1> ... <actor11>   the actor: 1 Pilot, 2 Copilot, 3 Doctor, 4 HEMS1, 5 HEMS2, 6 Technician male, 7 Technician female, 8 Dispatcher, 9 Ground unit, 10 Patient / extra, 11 Checklist.
                           The voice and the usual speed of each actor are chosen in the Actors and voices tab, per language.
  <Microsoft David>        a voice by name (also <voice:ryan>); it wins over the actor voice. A name that is not installed: the actor voice speaks and the log says so.
  <speed:1.2>              speed, 0.5 to 3 (1 = the speed set for the actor)
  <pause:1>                silence of 1 s (0 to 5) between the part before the tag and the part after it, inside one text (square brackets work too: [pause:1]); the time the voice needs to render the next part counts. Not at the start of a text.
  <vol:70>                 volume 0-100 of this text, multiplied by the master volume of the Volume tab. Pass the mission volume with params: "<vol:{0}>" and [VOLUME_CREW].
  <radio> / <radio:off>    radio effect on / off: start beep, radio hiss, band filter, squelch tail (Radio effect tab)
  <beep:double>            beep type of the radio effect for this text: none, click, beep, double, chirp (or 0-4). Optional: without it the type chosen in the Radio effect tab is used.
  <radio:chirp>            shortcut: radio on with that beep type
  <ics> / <ics:off>        the wired intercom of the helicopter: a click, no beep, the intercom hiss of the Radio effect tab, no squelch tail
  <radio:ext>              a call on an external radio (dispatcher, hospital): double beep, external radio hiss, squelch tail
  The squelch tail is one of ten recordings of the end of a transmission (assets\squelch-tail, embedded in the program; drawn at random, never the same twice in a row). The wav files of the folder audio\squelch-tail (created with the ten defaults at the first radio call) replace them: add, remove or change files there; with no file the old synthetic burst is used.
  <zone:far>               a voice that comes from 30 to 100 m away, heard very low (the program adds it from the zone variables)
  <EN> <FR> <IT> <DE>      language of the text (also <language:it>); it picks the voices of that language
  <defaultactor:11>        from now on every text without an actor tag is spoken by actor 11 (the HEMS mission sends it once, for its checklists); <defaultactor:off> ends it
  <stop>                   with interrupt 1: stop everything
  <br> <b> <i> ...         HTML-like tags are ignored

An unknown word in brackets is taken as a voice name. A tag is at most 60 characters. Free texts that may contain brackets (a dispatch, a patient name) must go behind <plain> (section 4).

3. VOICE PACKS AND THE say TAG
------------------------------
A voice pack is an XML file in the voicepacks folder of the program with the texts of the mission messages, several options for each, so the crew does not repeat itself and the texts can be changed or translated without touching the mission. The mission sends the key of the message, the program chooses the option, the actor, the radio and speaks.

  <say:CPL.5>                              speak the message CPL.5 of the pack
  <say:D.CAS>2 injured//conscious//collision//broken leg
                                           the text after the tag is the argument list, separated by //: they fill {0} {1} {2} {3} in the option
  <say:CPL.5:3>                            force option 3 (to test)
  <radio><beep:chirp><say:PLT.30>          tags before say are kept: the mission decides the radio, the pack decides the actor
  <actor3><say:PLT.30>                     an actor tag before say wins over the actor of the message

Arguments are plain text: the characters < > [ ] are removed, so a free text cannot break the tags. Everything after <say> belongs to it, so use one say per speak. In JSON:

  {"speak":{"text":"<radio><say:D.CAS>{0}//{1}//{2}//{3}","params":[{"local":"N_INJ"},{"local":"STATE"},{"local":"CAUSE"},{"local":"SYMPTOMS"}]}}

A message that has no placeholder takes no arguments: {"speak":"<say:CPL.5>"}. A missing argument becomes empty and the log shows a warning. A key that is not in any pack speaks nothing and logs a warning.

Placeholders of the pack texts: {0}..{9} the arguments; {PLT} {CPL} {PAX1} {PAX2} {PAX3} {TECH} the job word of the role (Pilot, Copilot, Chief, Paramedic, Doc, Technician); {@PLT} {@CPL} {@PAX1} {@PAX2} {@PAX3} {@TECH} the name of the shift (random, or typed by the user in the Actors tab) or the job word when there is none: use the name where the text sounds more colloquial and the job word elsewhere. {TECH} is the technician of the scene (male or female, see below).

<plain>   speak the text after the tag as it is, without any tag: <actor8><radio><plain>{0} with the dispatch text in params. The characters < > [ ] are removed. A free text that a translated voice pack should be able to replace does not use <plain>: it uses a key (next paragraph).

<text:KEY>English text//value//value   a free text of the mission with a key, for texts that exist in English in the mission (a checklist row, a dispatch description). The active voice pack may have a message KEY with the same words in its language and then speaks that; otherwise the English text is spoken as it is. {"speak":"<text:CHK.interiorckl.02>Baggage, cargo and loose items: stowed and secured!"}. The tags before it keep working: {"speak":{"text":"<actor8><radio><text:DSC.{0}.{1}>{2}","params":[...]}}. The values after the English (separated by //) fill {0} {1} of the pack text; the English must be the filled sentence. The key is never changed once used; a new text gets a new key. A pack of the Companion is checked against the base pack EN: the mission repository lists its texts and keys (tools/voice-texts.json) and the base pack holds the same ones.

Lists of vehicles: {list:N} and {be:N} take argument N made of the letters A (ambulance), F (fire engine), P (police): "AFP" gives "ambulance, fire engine and police" and "are". Example: {"speak":{"text":"<say:DSP.VEH.BOTH>{0}//{1}","params":["A","FP"]}} says "fire engine and police are already on scene, ambulance is on the way" (argument 0 is the vehicles on the way, argument 1 the vehicles on scene, as the pack message uses them).

Settings profiles (Service tab, group Settings profile; `src/Profiles.cs`; `profiles/Recommended.json` is a resource of the program): a profile (`ProfileData`) has the voices, the radio, the volumes and the options but not the paths, the audio devices, the port or the update data of the PC; its fields have the names of the settings file, so a copy of HPGMissionCompanion.json is a valid profile. `Profiles.Plan` says what is missing (Piper engine, voices; the list of voices is read from the cache or downloaded), `Profiles.Download` fetches it, `Profiles.Finish` backs the settings up (`backups\settings`), applies the profile, sends a missing voice back to the default, saves and aligns the entry of the simulators. The window (`RunProfile`) and the command line (`--oneclick <report> [--profile <file>] [--no-download]`, used by the installer with `/oneclick`) share it. Audio output (advanced options of the Volume tab, setting AudioOut): the headset category (intercom, radio, external radio, notifications) and the external category (voices in the air) each have an output device (the classic wave out of Windows, winmm.dll; "" = the Windows default) and a level; the notifications (sound files) have their own level and use the headset device; a level multiplies the other volumes. `--audio-devices <report>` lists the devices. Volume: the program follows the volumes of the mission (L:VOLUME_CREW, L:VOLUME_CHK) and lowers a voice (not a radio call) by 12 dB when the view is outside and the speaker is aboard, by 9 dB when the view is inside and the speaker is outside (Volume tab). The level is applied when a line starts, not while it plays.

Names of the crew: by default the job words (Pilot, Copilot, Chief, Paramedic, Doc, Technician; a pack can change them in its <names> list); {@PLT} gives a name only when there is one: the user typed it in the Actors tab (Name box next to the voice), or the option "Random names for each shift" draws one for each role from the static lists of first names of the program (the male technician takes a male name, the female one a female name). The mission only says <names:random> at a new shift; <names:PLT=Mark,CPL=Anna> forces names, <names:off> removes them.

Shared data (hangar and hospital waypoints of the R&E mission and changes to the offline databases of HPG): kept on GitHub, written by the program as `table_R&E_shared_waypoints.json` in the work folder of the mission; the mission reads it (macro `shared wps load`) and reloads it when `L:HMC_DATA_REV` changes. See docs/architecture/SHARED_DATA.md.

Options (pack EN 6): every message has six options. Option 1 is the default (the wav text, corrected) and stays so; 2 and 3 are professional variants; 4 to 6 are more and more colloquial where the sentence allows it, so that a filter on the option number can later cut the small talk. A futile message (futile="1": greetings, jokes, small talk nobody needs) starts at 3: options 1 and 2 are silent (<o silent="1"/>), 3 is the default and 4 to 6 the others; the lines of a group of futile messages are always coherent (same option number in every line), so a silent option makes the whole conversation silent. Callouts (numbers, clock positions, Vy, Vtoss, VNE) repeat the same word on purpose. A message with options for the technicians has six for each sex (a futile one two silent options shared by both and four for each sex).

Queue (2026-10-18): the lines of the radio, of the crew (intercom and voices), of the checklist (actor 11) and the sound files each have a lane and speak together, one line at a time per lane; an actor never speaks twice at once. The lines wait by priority: 3 time critical (callouts, warnings), 2 operations (default), 1 conversations, 0 small talk and tips. A line that waited longer than its age is dropped (callouts a few seconds, conversations 20 to 45 s, the dispatch and the checklists never), a later message of the same subject replaces the earlier ones still waiting (the cabin: preparing, secured, ready), and a time critical line cuts small talk. A pack message can say it with prio="0-3", age="seconds", topic="name" and rank="n"; without them the program decides by the key and the flags of the message, so a translated pack needs nothing. Interrupt cancels the lane of the text (a checklist line: only the checklist lane).

Voices at the same time (user request of 2026-10-19, setting `OverlapMode`, Volume tab, part of the profiles): 1 = turns (default): the speech lanes (radio, crew, checklist) take turns, a better line of another lane goes first, and only the sound files and the time critical lines (priority 3, callouts and warnings) overlap the speech; 2 = everything together (the behaviour of 0.36, lanes speak together); 0 = one line at a time in the whole program, the best waiting line first. The old boolean `OverlapLanes` (JSON only, no menu) is read once: true becomes 2, false becomes 0, and it is kept in sync. The log of 2026-10-19 (71 lines, 219 s of speech) had 9 overlapping pairs, all radio with crew (about 21 s), 3 notification sounds over speech, and bursts at the dispatch reading (the dispatcher on the external radio while the crew triage ran on the intercom). `--queue-test <report> --log <hmc log> --pack EN.xml` replays a log with the old queue, with all together and with turns and prints the seconds of overlapped speech and the waits; on that log: all together 28 s overlapped, turns 0 s (but more lines waiting over 10 s: 11 instead of 4, one line dropped as too old).

Options, helpers and radio rules added after the flight of 2026-10-20 (sources, not released):
- A message does not use again one of its last options (`PickPosition`: the last three with six options, the last one with three or fewer), also inside the coherent tone of a group: the same sentence comes back only after a few others.
- The plain text of a crew member without any effect tag (the readback of a dispatch, `<actor2><text:DSC...>`) follows the radio rules like a pack message (`Say.ApplyDelivery`): the intercom when powered, the voice when not, the radio between inside and outside; the pack messages `DSA.RB.A` and `DSA.RB.Q` say `radio="ics"`.
- A placeholder at the start of a sentence (the start of the text or after . ! ?) starts with a capital letter whatever its helper says (`{hr:0}` = "one heli rescuer" after a full stop).
- New helpers: `{hrp:N}` and `{hrs:N}` (the pick-up place of the heli rescuers and who answers on the radio; the argument N is the kind A alpine rescue station, F fire station, S sports field, M meeting point, the next argument is its name; the names PL_A, PL_F, PL_S, PL_M, HRS_A, HRS_F, HRS_S, HRS_M and the formats PL_OF and HRS_OF belong to the pack), `{comp:N}` (N, S, E or W: COMP_N ...), `{organ:N}` (H heart, L liver, K kidneys, U lungs, P pancreas: ORG_H ...), `{name:N}` (a name written in capitals, ROSSI, is said as Rossi).
- Scenario of the new messages: `tests/flight-2026-10-20-lines.txt` (`--expand <report> --lines tests/flight-2026-10-20-lines.txt`).
