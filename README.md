# PopRunner

PopRunner is a Windows desktop companion that brings the Prince from DOS Prince of Persia to your taskbar. Its tray app plays transparent, click-through animations of running, sliding turns, potion drinking, Princess greetings, and careful steps. A separate Windows Forms sample lets you inspect and tune the same animations.

| Project | Purpose |
| --- | --- |
| [PopRunner](src/PopRunner) | Native AOT tray app using Win32 calls. |
| [PopRunner.Core](src/PopRunner.Core) | Shared animation engine, configuration, and embedded sprite resources. |
| [PopRunner.Sample](src/PopRunner.Sample) | Windows Forms preview with playback controls and alignment guides. |
| [PopRunner.ExtractAssets](src/PopRunner.ExtractAssets) | Console tool that extracts the required artwork from a licensed game copy. |
| [PopRunner.Checks](tests/PopRunner.Checks) | Core behavior, configuration, and resource tests; supports Native AOT. |
| [PopRunner.ExtractAssets.Checks](tests/PopRunner.ExtractAssets.Checks) | Extraction checks across game versions, including image comparisons and failure handling. |

## 🛠️ Setup

### Requirements

- Windows and the **.NET 11 SDK**, selected by [global.json](global.json). Projects use C# 15.
- Windows C++ build tools for the Native AOT tray app.
- A licensed copy of **DOS Prince of Persia 1.0, 1.1, 1.3, or 1.4** to supply the artwork.

The repository excludes copyrighted game assets. Generate them locally before building either desktop app. The extractor builds independently of Core and its assets; no third-party NuGet packages are needed.

### Extract the artwork

Run from the repository root, replacing the example game path with your own:

```powershell
dotnet run --project src/PopRunner.ExtractAssets -c Release -- "C:\Games\Prince" "src\PopRunner.Core\Assets"
```

The game directory must contain `KID.DAT`, `SHADOW.DAT`, `PV.DAT`, and `PRINCE.DAT`. This creates the 318 required PNGs in the ignored Assets directory. See [asset extraction details](docs/implementation.md#asset-extraction) for formats, hash warnings, and error handling.

### Build the apps

```powershell
dotnet publish src/PopRunner -c Release -o artifacts/native
dotnet publish src/PopRunner.Sample -c Release --self-contained false -o artifacts/demo
```

Open [PopRunner.slnx](PopRunner.slnx) to work on the solution. The published tray app is standalone; the sample requires the .NET 11 Desktop Runtime. Both embed their configuration and locally generated images.

## 🎮 Use

### Tray app

Double-click **[Launch.cmd](Launch.cmd)** to start the tray app, then **double-click its miniature Prince icon** to play a random sequence. Right-click the icon for:

- **Play Random**: play a random sequence from the configured pool.
- **Play**: select a particular sequence.
- **Automatic Trigger**: choose Hourly, Daily, Weekly, or Disabled. Weekly is the default; your selection is saved for future runs.
- **About** and **Exit**.

Animations appear along the primary taskbar, pass mouse clicks through to windows underneath, and disappear when finished. Automatic playback checks for an unlocked, recently used session and microphone activity before making a random draw. The selected frequency is an average over eligible use, rather than a fixed schedule; see [automatic playback details](docs/implementation.md#automatic-playback).

### Windows Forms sample

Double-click **[Launch.Sample.cmd](Launch.Sample.cmd)**, or run:

```powershell
dotnet run --project src/PopRunner.Sample -c Release
```

| Control | Action |
| --- | --- |
| Sequence dropdown or **1–9, 0** | Select a sequence; **0** selects sequence 10. |
| **Space**, **R**, **Right arrow** | Play/pause/replay, restart, or advance one tick. |
| Speed control | Adjust playback from 0.25× to 2×. |
| **G** / Alignment guides | Show the root position, floor, grid, and sprite bounds. |
| Cycle all sequences | Move to the next sequence when the current one finishes. |
| **F5** / Load defaults | Reload the embedded configuration and restart. |
| View JSON | Inspect the embedded configuration in a read-only viewer. |

The frame strip highlights the current pose. Both apps share the same ten sequences; see the [numbered sequence list and animation notes](docs/animation-reference.md).

## ⚙️ Configuration settings

Edit **[animation.json](src/PopRunner.Core/animation.json)**, then **rebuild and restart** to apply changes. The same applies after regenerating images. The sample's Load defaults command reloads what is embedded in the running build.

| Setting | What to adjust |
| --- | --- |
| `desktop.spriteScale` | Tray sprite size at 96 DPI; default **2**, adjusted for monitor DPI. |
| `desktop.taskbarGapPixels` | Gap above the taskbar in physical screen pixels; default **0**. |
| `desktop.actionEdgeFraction` | Position of the slide, potion, or Princess relative to the entry edge; default **1/3** of the monitor width. |
| `desktop.randomScenarioIds` | Sequences eligible for random and automatic playback; defaults to all ten. |
| `clock.ticksPerSecond` | Default animation rate, **12 ticks/second**; greetings specify their own **10 ticks/second**. |
| `scenarios.*.startDelayTicks` | Delay before the entrance. Potion and greeting scenes use two seconds; potion bubbles animate during the wait. |
| `potionGraphics.alignWithFeet` | Align the potion with the feet. Enabled in the supplied JSON; disable to restore its original DOS placement. |
| `clips.*.frames`, `scenarios.*.steps` | Pose timings, movement, and sequence choreography. |

Default-valued properties are omitted. Sprite entries accept a filename or object, frames accept a number or object, and sequence steps accept a clip name or object. See the **[configuration reference](docs/configuration.md)** for all settings, shorthand examples, units, and timing rules; [AnimationConfig.cs](src/PopRunner.Core/AnimationConfig.cs) defines the defaults and validation.

The automatic-trigger frequency is changed through the tray menu and stored in the user's registry, separately from the embedded JSON.

## 🧪 Tests

After setup, build the solution and run the Core checks:

```powershell
dotnet build PopRunner.slnx -c Release
dotnet run --project tests/PopRunner.Checks -c Release
```

Core tests use small independent fixtures for engine behavior and inspect the actual embedded resources. They do not duplicate the choreography in `animation.json`.

See **[testing instructions](docs/testing.md)** for Native AOT checks, tray integration checks, sample UI captures, and extraction comparisons across all four supported game versions. Native self-test code is excluded from normal tray builds and enabled explicitly with `EnableNativeChecks=true`.

## 🔍 Advanced details

- [Implementation details](docs/implementation.md): desktop rendering and DPI, resource loading, JSON conversion, automatic playback, and asset extraction.
- [Animation reference](docs/animation-reference.md): sequence choreography, sprite alignment, original clocks, and careful-step reversal.
- [Research and provenance](research/provenance.md): DOS data formats, resource IDs, known hashes, frame tables, and original-game references.
- [AnimationEngine.cs](src/PopRunner.Core/AnimationEngine.cs) and [AnimationEntryJsonConverters.cs](src/PopRunner.Core/AnimationEntryJsonConverters.cs): playback and JSON shorthand implementation.

The tray uses Win32 calls and Native AOT; the sample uses Windows Forms and System.Drawing. Both use Core's fixed-timestep engine. The sample's floor and background scenery are drawn by the application.

## 📄 License

Project source code is licensed under the **[MIT License](LICENSE)**, copyright 2026 Shane Krueger.

Prince of Persia artwork, characters, and game data remain copyrighted by their respective rights holders and are not covered by that license. PopRunner is intended for **personal, recreational, non-commercial use** with a licensed game copy. Do not redistribute original game files, generated images, or builds that embed them. See **[third-party notices](THIRD-PARTY-NOTICES.md)** for the project policy and reference acknowledgments.
