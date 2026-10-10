# Third-party software

None of the following is included in this repository or in the built executable (`SimConnect.dll` is copied beside the executable in the runtime folder at build time, never committed). The app downloads it, on a user click, from the sources below.

| Component | Source | Licence | Used for |
| --- | --- | --- | --- |
| Piper engine 2023.11.14-2 (`piper_windows_amd64.zip`) | github.com/rhasspy/piper (release page) | MIT; it ships eSpeak NG (GPL) for the phonemes | text to speech |
| Piper voices | huggingface.co/rhasspy/piper-voices | each voice has its own licence in its `MODEL_CARD` (the app saves it as `voices\piper\<voice>.MODEL_CARD.txt`) | voices |
| Windows speech voices (OneCore) | installed in Windows | Microsoft | voices through `Windows.Media.SpeechSynthesis` |
| `SimConnect.dll` (MSFS SDK, `SimConnect SDK\lib`) | the Microsoft Flight Simulator SDK installed on the PC (`C:\MSFS 2024 SDK` or `C:\MSFS SDK`) | Microsoft, MSFS SDK licence | SimConnect link; copied next to the program by `tools\build.cmd`, never committed |
| HPG mission system and its `Update Mission Index.cmd` tool | the HPG H145 package of the user | HPG | the mission index update is run, never copied |

Before using or sharing a Piper voice outside your own PC, read its `MODEL_CARD`. The HPG voice server protocol is implemented from its public documentation (https://davux.com/docs/missionsys/TOPIC-SOUND.html).
