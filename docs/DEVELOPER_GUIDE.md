HPG MISSION COMPANION - SCRIPT DEVELOPER GUIDE
==============================================

Everything a mission author needs to make an HPG mission talk through this program. The same text is in the About tab of the program.

1. HOW A MISSION TALKS TO THE PROGRAM
-------------------------------------
The program is the "voice server" of the HPG mission system: a WebSocket server on localhost, port 5997 (Service tab). The traffic is one way, from the mission to the program. The mission connects once and then uses the speak command:

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

Options (pack EN 6): every message has six options. Option 1 is the default (the wav text, corrected) and stays so; 2 and 3 are professional variants; 4 to 6 are more and more colloquial where the sentence allows it, so that a filter on the option number can later cut the small talk. A futile message (futile="1": greetings, jokes, small talk nobody needs) starts at 3: options 1 and 2 are silent (<o silent="1"/>), 3 is the default and 4 to 6 the others; the lines of a group of futile messages are always coherent (same option number in every line), so a silent option makes the whole conversation silent. Callouts (numbers, clock positions, Vy, Vtoss, VNE) repeat the same word on purpose. A message with options for the technicians has six for each sex (a futile one two silent options shared by both and four for each sex).

Who can speak: the mission publishes L:HMC_CREW_MUTE (bit 0 pilot, 1 copilot, 2 doctor, 3 HEMS1, 4 HEMS2, as L:HMC_CREW_PRESENT): a dead crew member (or one with a life score under 15) does not speak; the program does not say the lines of that actor (the log shows "not said"). Small talk is also decided by the mission (voice calm): never with a dying patient, an injured crew member, after a death or in an emergency.

Technicians: the mission picks a male or a female technician for each scene (the start of a flight, the arrival at the base) and publishes it in L:HMC_TECH_SEX (0 male, 1 female). A pack message can have options for each: <o sex="m">..</o> and <o sex="f">..</o> (an option without sex is for both); the program uses the options of the technician of the scene and speaks them with actor 6 (Technician male) or 7 (Technician female). The crew lines that greet or answer the technician use {@TECH}.

Variety: <variety:random> (default) picks an option at random for every message and never the same option twice in a row for the same message. <variety:coherent> makes the messages of one conversation (same group="" in the pack) use the same option number, so the alternatives of a dialogue read as one dialogue; a new conversation starts after 3 minutes or with another group. Send the tag when the mission starts (it stays until changed).

Sound-only messages: a pack message with sfx="1" has no text, only a wav name; the program plays that file from the audio folder.

4. VERSIONS
-----------
Every pack has a whole version number that rises at every change of its texts. The mission declares what it needs at the start (or when the voice option is chosen):

  {"speak":"<pack:EN:1>"}          pack EN, at least version 1
  {"speak":"<dsp:3:Bolzano>"}      who the dispatcher is: group index of the mission (GRPID), city of the base; the pack says it with {DSP} (pack EN 14, see docs/architecture/VOICEPACKS.md)
  {"speak":"<mission:RE>"}         the identity of the mission (R&E here); the program keeps it and shows it in the log, other missions use their own short id

The program compares and tells the mission through simulator variables (SimConnect, it needs the MSFS SDK SimConnect.dll beside the program):

  L:HMC_VOICE_STATUS       0 nothing declared yet, 1 pack found and not older than needed, 2 pack not installed, 3 pack older than the mission needs (obsolete)
  L:HMC_VOICEPACK_VERSION  the version of the installed pack (0 = missing)
  L:HMC_APP_VERSION        the version of this program (0.4 -> 0.4)

  Example: {"wait_for":{"var":["L:HMC_VOICE_STATUS","number"]},"gt":0}  then {"if":{"var":["L:HMC_VOICE_STATUS","number"]},"ge":2, "then":[{"set_message":{"text":"Voice pack missing or obsolete: install it with the Mission Companion"}}]}

The log of the program shows the same as a warning. A newer pack than the mission needs is fine. When a newer version of a pack is installed, the old file moves to voicepacks\obsolete.

The program also keeps L:AUDIOINHIBIT at 1 (option in the Service tab) to silence the wav sounds of the aircraft.

5. WHEN TO USE THE RADIO
------------------------
In voice app mode the program decides how a line is heard from the context the mission publishes (section 10), so the mission adds no tag. In short: the dispatcher is an external radio call (double beep, more hiss), heard only with the battery on or the helicopter at the base; the crew uses radios (a call between inside and outside, or among crew when somebody is outside), the intercom (a click) when everybody is aboard and the battery is on, the voice when the battery is off; a technician next to the helicopter with the pilot aboard uses the intercom; technician, ground unit and patient speak by voice, heard from inside up to 30 m, very low up to 100 m, not beyond. With a plain speak text the tags are yours: <radio>, <ics>, <radio:ext> or <radio:off> before the text; a message with radio="on", "off", "ics", "ext" or "ext-near" in the pack applies it only when the mission sent no radio tag.

6. TIMING
---------
speak returns at once and the speeches wait in a queue, one after the other. A line is 15-18 characters of text per second of speech at speed 1.0. The pack keeps the options of a message at about the same length where the mission waits only a short time after it (tight="1"). If the mission fires lines faster than they are spoken they pile up: space them with sleep, or use interrupt for what must not wait. <stop> with interrupt 1 clears everything (for example when the player aborts).

7. THE PACK FILE
----------------
voicepacks\EN.xml (any file name; the pack name is the name="" attribute, or the file name when there is none; the installed copy is always saved as <pack name>.xml):

  <?xml version="1.0" encoding="utf-8"?>
  <voicepack name="EN" language="EN" version="1" title="English crew voices">
    <names>
      <name role="PLT">Pilot</name>   <name role="CPL">Copilot</name>   <name role="PAX1">Chief</name>
      <name role="PAX2">Paramedic</name> <name role="PAX3">Doc</name>
    </names>
    <message key="CPL.5" actor="2" radio="auto" group="" seq="0" lvar="L:CPLAND5" wav="AND.cpl.comm.chk.wav" sleep="2" tight="1" trigger="when ...">
      <o>Copilot here, comm check.</o>
      <o>This is the copilot, radio check.</o>
    </message>
  </voicepack>

voicepack: name (optional: letters, digits, _ -, up to 24; when it is missing the name of the file is used, so any XML file can be dropped in), language (EN FR IT DE), version (whole number from 1), title.
message: key (unique; letters, digits . _ -), actor 1-11, radio auto|on|off|ics|ext|ext-near, group (conversation name) and seq (position in it), sfx="1" for a sound-only message with a wav (vol="0-100" plays it at that level at most). lvar, wav, sleep, tight, trigger, draft are information for the author and the tools.
<o>: one text option (any number; 4-5 is good; sex="m" or "f" for the technicians). Texts may contain {0}.. {PLT}.. {@PLT}.. and the normal tags (for example <speed:1.1>).

Install a pack: drag the XML file onto the Voice packs tab (or Browse), or copy it into the voicepacks folder and press Reload. A pack of the same name and an older or equal version asks before replacing. Command line: HPGMissionCompanion.exe --install-pack file.xml.
The key of the messages of the HEMS pack: ROLE.NUMBER, with ROLE = PLT pilot, CPL copilot, PAX1 crew chief, PAX2 hoist operator, PAX3 medical crew, FRA the technician at the start of a flight, SARA the technician at the arrival (both with options for the male and the female technician), MAR marshal, HW emergency events, SYS system sounds. The number is the one of the wav pack LVAR (L:CPL<pack>5 -> CPL.5).

8. TESTING
----------
  HPGMissionCompanion.exe --expand report.txt --text "<pack:EN:1><radio:chirp><say:PLT.30>"       what the text becomes, no sound
  HPGMissionCompanion.exe --expand report.txt --lines lines.txt --repeat 5 --seed 3                 a list of texts in order (one per line)
  HPGMissionCompanion.exe --packs-check report.txt                                                   packs found and their problems
The Service tab has a box to try a text, and the log shows [RAW] (the text as it arrived), [SAY] (message and option chosen), [VOICE] (actor, voice, speed, volume, radio) and warnings. The standalone test mission tests\voicepack-say-test.json exercises every tag.

9. A SMALL MISSION
------------------
  "start": [
    {"connect_voice_server":{"on_connected":[{"speak":"<pack:EN:1><variety:coherent>"},{"speak":"<names:PLT=Mark,CPL=Anna,PAX3=Sue>"}],"on_disconnected":[]}},
    {"sleep":2},
    {"speak":"<radio><say:CPL.5>"},
    {"sleep":3},
    {"speak":{"text":"<actor8><radio><plain>{0}","params":[{"local":"DISPATCH_TEXT"}]}},
    {"speak":"<actor11><plain>Checklist: interior check."}
  ]

10. VOICE APP MODE: THE MISSION INCREMENTS LVARS
------------------------------------------------
The HEMS mission does not call speak for its crew lines. Each line is a counter: L:CPL{VCP}5 += 1, where {VCP} is the voice code of the mission. With a wav voice pack the code is the name of the pack and the aircraft sound.xml plays the wav. When L:AUDIOINHIBIT is not 0 (the program sets it to 1: option of the Service tab) the mission uses the code APP: no sound.xml knows L:CPLAPP5, so no wav plays, and the program, which watches those variables through SimConnect, speaks the message of the pack whose lvar template is L:CPL{VCP}5.

  message lvar="L:CPL{VCP}5"   -> read as L:CPLAPP5 (the vcp="" attribute of the pack, APP when missing)
  a counter that rises          -> the message is spoken (random option, or the coherent one)
  first value after connecting  -> baseline: nothing is spoken; a reset to 0 is silent
  sounds without voice          -> sfx messages: the wav of the audio folder is played (dispatch ringtones, new message tone, tablet alarm)
  a line waiting more than 30 s -> dropped ([DROP] in the log)

To add a message to the mission: choose a free number for the role (the pack lists the used ones), add the message to the pack (key ROLE.NUMBER, lvar="L:ROLE{VCP}NUMBER"), and in the mission increment L:ROLE{local:VCP}NUMBER where the line happens. Space the lines of a conversation by 0.4 s at least: the program sees the counters in order only if they change in different simulator frames.

How a line is heard is decided by the program from the variables the mission publishes: L:HMC_CREW_PRESENT and L:HMC_CREW_EXIST (bit 0 pilot, 1 copilot, 2 doctor, 3 HEMS1, 4 HEMS2: aboard, and existing), L:HMC_AT_BASE (1 = at the departure base, in wifi range), L:HMC_ZONE_TECH / L:HMC_ZONE_GROUND / L:HMC_ZONE_PATIENT (distance from the helicopter: 1 up to 5 m, 2 up to 30 m, 3 up to 100 m, 4 beyond), L:HMC_HELI_OFF, and L:HMC_CTX_VALID (1 = the context is published; without it no tag is added). The battery master is not published: the program reads the variable of the aircraft, L:H145_SDK_OH_BATTERY_MASTER (1 = on). The rules are in section 5. A message with radio="on", "off", "ics", "ext" or "ext-near" in the pack ignores them.

The checklists of the mission are untagged speak texts: the mission sends {"speak":"<defaultactor:11>"} once (voice code APP only, the old audio service would read the tag aloud) and the program speaks every text without an actor tag with actor 11.

Free texts do not go through counters: use speak with <plain> (the dispatcher reading the dispatch:  {"speak":{"text":"<actor8><radio><plain>{0}. {1}","params":[...]}} ) or <say:KEY>args. The crew names: the mission sends {"speak":"<names:random>"} at a new shift; the program draws names only when the user chose Random names in the Voice packs tab.