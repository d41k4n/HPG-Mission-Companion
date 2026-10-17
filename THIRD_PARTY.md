# Third-party software

Apart from the rows marked "Included", none of the following is included in this repository or in the built executable. `SimConnect.dll` is copied beside the executable in the runtime folder at build time, never committed; the installer `HPGMissionCompanionSetup.exe` embeds it when the machine that builds it has the MSFS SDK (Microsoft ships it for add-on developers to distribute with their add-ons), and the installer is a release asset, never a tracked file. The app downloads it, on a user click, from the sources below.

| Component | Source | Licence | Used for |
| --- | --- | --- | --- |
| Piper engine 2023.11.14-2 (`piper_windows_amd64.zip`) | github.com/rhasspy/piper (release page) | MIT; it ships eSpeak NG (GPL) for the phonemes | text to speech |
| Piper voices | huggingface.co/rhasspy/piper-voices | each voice has its own licence in its `MODEL_CARD` (the app saves it as `voices\piper\<voice>.MODEL_CARD.txt`) | voices |
| Community Piper voices (`it_IT` leonardo, giorgio, miro) | huggingface.co/kirys79/piper_italiano (CC BY 4.0), huggingface.co/OpenVoiceOS/pipertts_it-IT_miro (CC BY-NC-ND 4.0, non-commercial, attribution to TigreGotico Lda) | as listed; the app writes the source and the licence to `voices\piper\<voice>.MODEL_CARD.txt` | Italian voices; downloaded on a user click, never included |
| Windows speech voices (OneCore) | installed in Windows | Microsoft | voices through `Windows.Media.SpeechSynthesis` |
| `SimConnect.dll` (MSFS SDK, `SimConnect SDK\lib`) | the Microsoft Flight Simulator SDK installed on the PC (`C:\MSFS 2024 SDK` or `C:\MSFS SDK`) | Microsoft, MSFS SDK licence | SimConnect link; copied next to the program by `tools\build.cmd`, never committed |
| STT4SG (Swiss German TTS and Translation) | stt4sg.fhnw.ch/tts (i4ds, FHNW) | Non-commercial research demo licence (terms: stt4sg.fhnw.ch) | Swiss German dialect translation and neural speech synthesis (8 dialect voices marked "(STT4SG)"); used only when one of those voices is chosen, and then the text to speak is sent over the internet to the server of FHNW; the results are kept in `audio\cache\stt4sg`. Contributed by Daikan (GitHub d41k4n). |
| Included: squelch-tail recordings (`assets/squelch-tail`, 10 wav files, recordings of the end of ATC transmissions) | contributed by Daikan (GitHub d41k4n, integrated in 0.25); embedded in the program | the licence of the recordings is not stated by the contributor; they are used as short sound effects only | end of a radio transmission (the user can replace them in `audio\squelch-tail`) |
| Included: notification sounds (`assets/app-sounds`, 8 wav files `AND.*`) | the sounds of the R and E mission pack that its messages play | as the mission pack | tablet tones, new message tone, alarm (embedded in the program since 0.28) |
| Included: German voice pack (`voicepacks/DE.xml`) | translation of the base pack EN by Daikan (GitHub d41k4n), kept in step with the base pack | as this repository (see LICENSE); the texts of the base pack are by the author | German texts of the mission messages |
| HPG mission system and its `Update Mission Index.cmd` tool | the HPG H145 package of the user | HPG | the mission index update is run, never copied |

Before using or sharing a Piper voice outside your own PC, read its `MODEL_CARD`. The HPG voice server protocol is implemented from its public documentation (https://davux.com/docs/missionsys/TOPIC-SOUND.html).
