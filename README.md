# I TOLD YOU WHERE TO GO.

> **This game was fully generated with AI.**
>
> Every script, shader, scene, level, material and UI element in this project was created by an AI coding
> assistant ([Claude Code](https://claude.com/claude-code), by Anthropic), working inside the Unity Editor
> through the Unity MCP connection. The human developer only described what they wanted in plain-English
> prompts, such as *"add a square at 0 0"*, *"make the player look like an astronaut"* or *"use a shader to make
> the background look like outer space"*, and answered a few questions along the way.
>
> The AI wrote the C# gameplay code and the HLSL shaders, built the GameObjects and scenes, chose and
> installed the offline speech-recognition package, debugged problems (a silent microphone, Unity 6 sprite
> color changes, scenes that didn't save), tested features in Play mode by driving the game itself, and
> committed each step to git. No code or art was written or drawn by hand. Even this README was written by the AI.

---

## What is it?

A 2D physics game you play **entirely with your voice**. An astronaut floats in space. You tell it which
direction to launch and how hard, and try to land it on a small spinning Earth.

- Say an **angle** (0° = right, counting counterclockwise: 90° = up, 180° = left, 270° = down).
- Say a **power**.
- The game asks `Launch at 45 degrees with 10 power?`. Say **yes** to launch, or **no** (or new numbers) to change them.
- Land on the planet to win the level. Stay in the air longer for more points.

There are 3 levels, a main menu, and an end screen with your scores and high scores.

## Requirements

| | |
|---|---|
| Unity | **6000.3.10f1** (Unity 6.3 LTS), Universal Render Pipeline (2D) |
| OS | Windows (tested). Speech runs offline, so no internet is needed while playing. |
| Microphone | Any microphone. Quiet mics are boosted automatically. |
| Speech model | `ggml-base.en.bin` (about 148 MB, **not included in git**, see below) |

### Setup

1. Clone the repository and open the folder in Unity Hub with Unity **6000.3.10f1**.
2. Download the Whisper speech model **`ggml-base.en.bin`** from
   <https://huggingface.co/ggerganov/whisper.cpp> and put it here:
   ```
   Assets/StreamingAssets/Whisper/ggml-base.en.bin
   ```
   It's listed in `.gitignore` because it's larger than GitHub's 100 MB file limit.
3. Open `Assets/Scenes/MainMenu.unity` and press **Play**.
4. Keep the Unity window focused while playing. Unity pauses Play mode when the editor is in the background.

## How to play

| Where | Say | What happens |
|---|---|---|
| Main menu | **"Start"** | Starts Level 1 |
| In a level | **"45 degrees"** then **"10"** (or "45 degrees, power 10") | Sets angle and power |
| Confirmation | **"Yes"** | Launches the astronaut |
| Confirmation | **"No"**, or new numbers like "No, 60 degrees power 20" | Asks again with the new values, until you say yes |
| After a win | **"Yes"** | Next level |
| After a win | **"Retry"** | Replays the same level |
| End screen | **"Play Again"** | Starts a new run at Level 1 |

Numbers can be spoken as words or digits: "forty-five", "one hundred and twenty", "12.5", "a hundred".

### Rules

- The astronaut starts each level at a set position and doesn't move until launched.
- The launch is an **impulse applied at the torso's center**. The helmet is twice as heavy as the torso and sits
  above it, so the push is off-center and the astronaut **tumbles** as it flies.
- **Score:** you earn 100 points per second the astronaut is in the air without touching anything.
- **Out of bounds** (off the left, right or bottom of the screen): the score for that attempt is 0 and the astronaut
  goes straight back to its start. Flying above the top is allowed, because gravity brings it back.
- **Hitting the target:**
  1. The game goes into **slow motion** just before impact.
  2. It **freezes** at the moment of contact and a **red ring-in-ring marker** shows where they touched.
  3. The camera **zooms in fast, eases into place**, holds for 0.5 s, then zooms back out.
  4. The astronaut (which **shrinks as it gets closer** to the planet) **lands on the Earth** with its feet on the
     ground and **cheers** with its arms up.
  5. "Ayy, I see what you did there :)" appears, then after 1 second you're asked to continue or retry.
- After Level 3 the **End Game** screen shows this run's score for each level and the total, marks new bests,
  shows your previous high scores and recent runs, and lets you say "Play Again".

### Levels

Each level is its own asset in `Assets/Levels/`. Select one to change where the player and target are.

| Level | Name | Player start | Target |
|---|---|---|---|
| 1 | Warm Up | (-5, 0) | (0, -4.25) |
| 2 | Long Shot | (6, 0) | (-6, -4.25) |
| 3 | Up and Over | (-7, -3) | (7, 3) |

Scores are saved with `PlayerPrefs`: the last and best score per level, the best run total, and the last 5 run totals.

## How it works

### Speech recognition

`MicrophoneSpeechListener` records the default microphone with Unity's `Microphone` API. It detects phrases by
comparing loudness to the background noise level, amplifies quiet phrases (up to 60×), and transcribes each
phrase **offline** with [whisper.unity](https://github.com/Macoron/whisper.unity) (OpenAI's Whisper model running
through whisper.cpp). Each recognized phrase is raised as an event:

- On the menus, `VoiceSceneLoader` checks phrases for "start" or "play again" and loads the next scene.
- In a level, `GameManager` receives phrases. It handles "yes" and "retry" after a win and passes everything else
  to `PlayerLauncher`.
- `SpokenNumbers` turns text like "two hundred seventy degrees" into numbers and recognizes yes, no and retry.

Unity's built-in Windows dictation was tried first, but it never returned any words with this project's quiet
microphone, so it was replaced with Whisper plus automatic gain.

### Scenes

| Scene | Contents |
|---|---|
| `MainMenu` | Title, large astronaut and Earth, "Say Start to begin" |
| `Game` | The 3 levels, score HUD, win message and prompt, impact camera |
| `EndGame` | Run scores, previous high scores, astronaut and Earth, "Say Play Again to restart" |

Build order: MainMenu → Game → EndGame.

### Scripts (`Assets/Scripts`)

| Script | Job |
|---|---|
| `MicrophoneSpeechListener` | Mic capture, phrase detection, gain, Whisper transcription, `PhraseRecognized` event |
| `SpokenNumbers` | Parses spoken numbers and yes / no / retry |
| `PlayerLauncher` | Angle → power → confirm flow, impulse launch, airtime score, out-of-bounds, slow motion, shrinking, landing and cheering |
| `Target` | The planet trigger; tells the player it was hit |
| `GameManager` | Loads levels, routes speech, win message and continue prompt, sends you to the end screen |
| `LevelData` | ScriptableObject with a level's name and player and target positions |
| `ScoreBoard` | Top-left HUD: level, live score, saved level scores |
| `ScoreRecords` | Saved scores (PlayerPrefs) and the current run's scores |
| `ImpactCamera` | Freeze, impact marker, ease-out zoom, hold, zoom back out |
| `VoiceSceneLoader` | Loads a scene when a spoken command matches |
| `EndGameScreen` | Fills in the end screen's score tables |

### Shaders (`Assets/Shaders`)

All three are hand-written HLSL for URP and use **3D Perlin noise** (fractal Brownian motion). None of them need a texture.

| Shader | Look |
|---|---|
| `EarthPlanet` | Turns a flat circle sprite into a lit 3D sphere: oceans, green continents, polar ice and drifting clouds, a 23.4° tilted axis, **rotating to the right**, a day and night side, ocean shine and an atmosphere rim. |
| `WornSuit` | Makes the astronaut's suit look **old**: yellowed fabric, grime (heavier toward the bottom), sun-faded patches, fabric grain and scratches. |
| `SpaceBackground` | Deep space with nebula clouds, a Milky Way band and three layers of stars plus a few bright flared ones. It's computed in **screen space with no time input**, so it never moves, even during the impact zoom. |

Each shader's look can be tuned on its material in `Assets/Materials`.

### The astronaut

The astronaut is built only from Unity's default **circle and square sprites**: helmet, gold visor with depth and
a glint, torso, backpack, arms, gloves, legs, boots, belt and a chest panel with lights. All the parts share one
`Rigidbody2D` with auto mass:

- **Torso** (square, density 1): mass 0.36
- **Helmet** (circle, density set so its mass is exactly **2×** the torso): mass 0.72
- Backpack, arms, legs and boots have colliders for their shape but almost no mass (0.006 in total).

The center of mass ends up about 0.34 units above the torso center, which is what makes launches tumble.

## Tuning

Most behavior is exposed in the Inspector:

- **Player** (`PlayerLauncher`): power multiplier, points per second, slow-motion speed and look-ahead, minimum size near the target, cheer angle and speed, bounds margin.
- **Main Camera** (`ImpactCamera`): zoom size, zoom-in, hold and zoom-out times, marker size.
- **SpeechListener** (`MicrophoneSpeechListener`): model path, language, phrase detection thresholds, gain, **Verbose Logging** (shows mic levels in the Console).
- **GameManager**: level list, win text, delays.

## Troubleshooting

- **Nothing happens when I speak:** tick **Verbose Logging** on `SpeechListener` and watch the mic level in the Console. If phrases never start, lower **Min Speech Rms**. Also check the Windows input volume and that the mic is in the right jack.
- **An error about loading the Whisper model:** the `ggml-base.en.bin` file is probably missing or in the wrong folder; see Setup.
- **The game seems frozen:** click the Unity window. Play mode pauses while the editor is in the background.
- **`Error reason is 'NoSubscription'` in the Console:** this comes from Unity's AI Assistant package (used for the
  AI's editor connection) when the account has no Unity AI subscription. It's harmless and unrelated to the game.

## Third-party

- [whisper.unity](https://github.com/Macoron/whisper.unity) 1.4.0 (MIT), embedded in `Packages/com.whisper.unity`.
- Whisper `ggml-base.en` model from [whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) (MIT), downloaded separately.
- TextMesh Pro essential resources (Unity).
