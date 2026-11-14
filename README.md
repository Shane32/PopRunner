# PopRunner

Project source code is licensed under the [MIT License](LICENSE), copyright 2026 Shane Krueger. This license does not cover the original Prince of Persia game materials; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

PopRunner is intended for **personal, recreational, non-commercial use** with a copy of DOS Prince of Persia that you are licensed to use. The game artwork and data remain copyrighted by their respective rights holders. This Git repository excludes the copyrighted assets. Use the asset extraction tool with your licensed copy to create them locally; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

**Before building either desktop app**, follow [Extract source artwork](#extract-source-artwork) to populate `src/PopRunner.Core/Assets`. The .NET 11 console extractor builds independently of Core and its assets. Local app builds embed those generated images.

`src/PopRunner` is a .NET 11 Native AOT tray app using Win32 DLL calls. It has no Windows Forms, WPF, System.Drawing, or COM dependency. The miniature Prince tray icon uses the original sprite artwork. **Double-click** it to randomly play any of sequences **1–10**; a single click does not start playback. The right-click menu has bold **Play Random**, a **Play** submenu listing all configured sequences, **Automatic Trigger**, **About** (a native message box that states "Not for distribution"), and **Exit**. Selecting another animation restarts playback immediately.

**Automatic Trigger** offers **Hourly**, **Daily**, **Weekly** (the default), and **Disabled**, with a radio mark on the selected option. While enabled, a Win32 timer checks once every 60 seconds. It skips playback unless the current Windows session is active and unlocked, keyboard or mouse input occurred within the last five minutes, no microphone use is recorded, and no sequence is already playing. Eligible checks make one independent random draw:

| Selection | Chance per eligible minute | Expected frequency |
| --- | --- | --- |
| Hourly | 1/60 | Once per hour of eligible use |
| Daily | 1/480 | Once per eight hours of eligible use |
| Weekly | 1/2400 | Once per forty hours of eligible use |
| Disabled | No checks | Manual playback only |

A successful draw uses the same configured random sequence pool as **Play Random**. These are averages, so animations can occur close together or farther apart; skipped minutes do not accumulate. Changing the selection takes effect immediately, with the first check one minute later. The selection is stored as DWORD **`AutomaticTrigger`** under **`HKEY_CURRENT_USER\Software\PopRunner`** (`0` disabled, `1` hourly, `2` daily, `3` weekly); absent or invalid settings use Weekly.

The activity gate uses [WTS session state](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w) and [GetLastInputInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo). Microphone detection reads the user and machine `CapabilityAccessManager\ConsentStore\microphone` registry trees, including packaged apps and `NonPackaged`. A positive `LastUsedTimeStart` without a matching completed `LastUsedTimeStop` suppresses playback. This Windows privacy history is a best-effort indicator: untracked apps may be missed and stale records may suppress playback after a crash. Unreadable or malformed activity records also suppress the automatic attempt; the app tries again next minute.

The Prince moves along the top of the primary taskbar in a topmost, nonactivating, per-pixel-alpha window. The floor baseline meets the taskbar; sprites retain their original per-pose foot movement. Every pixel passes mouse input to the windows underneath, including opaque sprite pixels. The window is bounded by the primary monitor, so sprites clip at its edges. It hides when the sequence finishes, leaving the tray icon available for another run. Slides, potions, and the Princess are placed about one-third of the monitor width from the edge the Prince enters. Potion and greeting sequences first show the potion or Princess for two seconds before the Prince starts running; potion bubbles animate throughout this delay. Careful-step sequences begin just outside the monitor, step fully into view, hold for four seconds, and back out through the same edge without turning. Routes adapt to the monitor while preserving original per-pose movement and animation clocks. The Princess waits for the longer approach before the original reunion handoff. All desktop adaptations use copies of the source scenes; the sample's routes are unchanged.

Double-click **`Launch.cmd`** to run `artifacts/native/PopRunner.exe`. The published native executable needs no installed .NET runtime or loose assets. To build it, use the .NET 11 SDK and Windows C++ build tools:

```powershell
dotnet publish src/PopRunner -c Release -o artifacts/native
```

AOT and trimming analyzers are enabled in both PopRunner and Core. Native publishes use the runtime/compiler patch selected by the .NET 11 SDK; update the SDK and republish to incorporate newer patches. Its `LibraryImport` declarations and unmanaged window callbacks are compiled ahead of time. PNG handling uses managed byte buffers and `ZLibStream`; GDI DIBs and `UpdateLayeredWindow` present the transparent sprite without a UI framework.

Desktop settings live in the embedded `src/PopRunner.Core/animation.json`:

| Setting | Meaning |
| --- | --- |
| `desktop.spriteScale` | Sprite scale at 96 DPI; default **2**, scaled for monitor DPI |
| `desktop.timerIntervalMilliseconds` | Native timer wake-up interval; default **15**, independent of the animation tick rate |
| `desktop.taskbarGapPixels` | Physical screen-pixel gap between the feet baseline and the top of the taskbar; default **0** |
| `desktop.actionEdgeFraction` | Distance of the slide, potion, or Princess from the entry edge, as a fraction of monitor width; default **1/3** |
| `desktop.randomScenarioIds` | Eligible scene IDs for double-click and **Play Random**; defaults to all sequences **1–10** |
| `stage.pixelAspectY` | DOS pixel aspect correction, also applied by the desktop renderer |
| `potionGraphics.alignWithFeet` | Align the potion bottom with the feet; enabled in the supplied JSON. Disable to restore the original DOS placement |
| `scenarios.*.startDelayTicks` | Delay before the Prince starts; potion scenes use **24** ticks and greetings **20**, both two seconds at their respective clocks |
| `scenarios.*.desktopEdgeEntry` | Place a finite step-and-return route just outside its entry edge; enabled for the two careful-step sequences |

Rebuild and restart after editing embedded settings. Normal builds exclude the native self-test code. To include it, set **`EnableNativeChecks=true`**, which defines `POP_RUNNER_CHECKS` for the `#if` blocks. This option works independently of Debug/Release. Publish a separate check-enabled executable, then run it from the interactive Windows desktop:

```powershell
dotnet publish src/PopRunner -c Release -p:EnableNativeChecks=true -o artifacts/native-with-checks
artifacts\native-with-checks\PopRunner.exe --self-test artifacts\native-checks
```

The enabled build checks all 318 locally generated sprites, pixel fidelity and compositing, random selection, the bold default menu entry and numbered submenu, automatic-trigger menu selection/persistence/gates/odds/timer dispatch, monitor/taskbar geometry, click-through behavior through opaque and transparent pixels to a separate test process, all configured desktop sequences, placement of interaction sprites, greeting synchronization, replay, the Win32 timer, and Exit. Automatic-trigger checks inject activity and random draws, exercise microphone-tree traversal using temporary registry data, and clean up their temporary preference keys without modifying the user's selection. Scenario checks derive pose and timing expectations from the loaded configuration; the separate Core fixtures test engine behavior independently. It writes `native-smoke-checks.txt`, `native-check-progress.txt`, and transparent PNG previews. It automatically dismisses its popup menu; it does not open the About dialog. During the input test, it briefly shows its own target window, clicks only that window, and restores the mouse position. An isolated desktop without Explorer cannot run the shell tests. Omitting the property (or setting it to `false`) excludes these checks, their helper Win32 imports, and overlay diagnostics; `--self-test` is unavailable in normal builds. The separate Core check project remains available regardless of this native-app build option.

## Windows Forms sample

A .NET 11 Windows Forms desktop demo using standard Windows controls and a double-buffered GDI+ sprite surface. The extraction tool can generate 251 Prince, 59 Princess, and 8 potion PNGs from a licensed game copy into `src/PopRunner.Core/Assets`. A local build embeds the configuration and those 318 PNGs in `PopRunner.Core.dll` and reads them as resource streams. Running sequences enter from offscreen; the careful-step sequences start just outside an edge:

1. Run from left to right.
2. Run from right to left.
3. Run in from the left, slide into a reversal, and return left.
4. Run in from the right, slide into a reversal, and return right.
5. Run up to the Princess from the right, greet her, and embrace using the original reunion sprites.
6. Run up to the Princess from the left, greet her, and embrace using mirrored reunion sprites.
7. Run in from the right, stop, drink a large red potion, turn right, and run back out right.
8. Run in from the left, stop, drink a large red potion facing right, turn left, and run back out left.
9. Take one careful step right from the left edge, stand for four seconds, then reverse the step back offscreen.
10. Take one careful step left from the right edge, stand for four seconds, then reverse the step back offscreen.

## Run

Double-click **`Launch.Sample.cmd`**. It starts the sample in `artifacts/demo/PopRunner.Sample.exe` with its embedded configuration and sprites.

Windows with the .NET 11 Desktop Runtime is required. To build and run from source, install the .NET 11 SDK and generate the assets first:

```powershell
dotnet run --project src/PopRunner.Sample -c Release
```

Open `PopRunner.slnx` to work on the solution. `global.json` selects the .NET 11 SDK, and the `net11.0` targets use C# 15, including its type-union support. No third-party NuGet packages are needed.

## Controls

| Control | Action |
| --- | --- |
| 1–9, 0 or sequence dropdown | Select and restart a sequence (`0` selects sequence 10) |
| Space | Pause / play / replay |
| R | Restart the selected sequence |
| Right arrow | Pause and advance one simulation tick |
| Speed numeric control | Play at 0.25× through 2× |
| G | Show the DOS root position, floor line, grid, and sprite bounds |
| F5 / Load defaults | Load the embedded defaults and restart the selected sequence |
| View JSON | Show the embedded configuration in a read-only viewer |
| Cycle all sequences | Advance to the next sequence on completion |

The frame strip follows the active clip and highlights the current pose. Turn off cycling to stop at the end of the selected sequence; Replay starts it again. Number and arrow shortcuts allow normal editing while the speed control has focus.

## Adjusting motion

Edit **`src/PopRunner.Core/animation.json`**, then **rebuild and restart the app** to use the changes. The same rebuild and restart is required after changing images in `src/PopRunner.Core/Assets`. F5 reloads the defaults embedded in the running build; View JSON displays those defaults read-only. JSON comments and trailing commas are accepted; unknown property names are rejected to catch typos.

Properties and sections that match the defaults in [AnimationConfig.cs](src/PopRunner.Core/AnimationConfig.cs) are omitted from the supplied JSON. Add any documented setting to override its default. For example, `{ "clip": "end" }` uses `startFrameIndex: 0`, `repeat: 1`, `untilX: null`, and `exitOnFrames: []`. Omitted values follow the C# defaults if those defaults change.

Three entry types accept either shorthand or an object. Sprite values may be a unique PNG filename, such as `"res407.png"`, or a sprite object with `image` and any nondefault alignment metadata. Both forms also accept full logical paths such as `"Assets/Prince/res407.png"`. Clip frames may be a frame number, such as `15`, or an object such as `{ "frame": 8, "dx": 5 }`. Sequence steps may be a clip name, such as `"end"`, or an object such as `{ "clip": "run", "untilX": 120 }`; this applies to the Prince and supporting actors. Shorthand uses the same defaults as the corresponding object. The supplied JSON writes each sprite, frame, or step object on one line. Images remain in their `Prince`, `Princess`, or `Potion` folders; bare filenames resolve to the matching embedded resource.

Core represents these input alternatives with C# type unions and normalizes them once while loading. It also resolves sprite filenames to their full logical resource paths before validation. The animation engine and callers continue to use the existing sprite, frame, and step objects. JSON conversion uses source-generated metadata and remains compatible with Native AOT.

| JSON location | Meaning |
| --- | --- |
| `clock.ticksPerSecond` | Fixed animation rate; default **12** |
| `clock.maxCatchUpSeconds` | Maximum elapsed time processed after a UI stall |
| `stage.width`, `height`, `floorY` | Native pixel dimensions of the single-floor preview and the actor's DOS baseline |
| `stage.pixelAspectY` | Vertical pixel correction; **1.2** approximates a DOS CRT, **1** uses square PNG pixels |
| `stage.movementUnitPixels` | Screen pixels per DOS movement unit; default **16/7** |
| `stage.preStretchPixelSize` | One original pre-stretch pixel in screen space; default **8/7**, used for the original parity adjustment |
| `stage.quantizeToPixels` | Round rendering positions using the original integer truncation |
| `stage.showGuides`, `autoAdvance` | Initial guide and cycling settings |
| `potionGraphics.bodyFrame`, `bubbleFrames` | Original large vessel and ordered red bubble sprite IDs |
| `potionGraphics.ticksPerFrame`, `initialBubbleFrame` | Bubble animation hold and zero-based starting phase; defaults **1** and **0** |
| `potionGraphics.alignWithFeet` | Optional vertical alignment override. Shifts bottle and bubbles together so the bottle bottom meets `floorY + 1`; false or omitted uses the original offsets |
| `potionGraphics.bodyOffsetX/Y`, `bubbleOffsetX/Y` | Native-pixel offsets from `potionX`; Y positions the sprite's exclusive bottom relative to `floorY + 1` |
| `sprites` | Original pose number → logical embedded PNG path, horizontal/vertical frame offsets, and odd-pixel alignment flag |
| `clips.*.frames` | Ordered poses, per-pose movement, hold duration, and optional potion events |
| `clips.*.exitDx`, `exitFlip` | Movement and facing change at a clip boundary |
| `scenarios` | Start position, direction, potion position, and the route's sequence of clips |
| `scenarios.*.ticksPerSecond` | Optional scenario-specific clock; the reunion defaults to **10**, matching the ending cutscene |
| `scenarios.*.startDelayTicks` | Initial delay in that scenario's animation ticks, **0–10000**; zero or omitted starts immediately. Potion bubbles keep advancing while actor scripts wait |
| `scenarios.*.desktopEdgeEntry` | Optional finite desktop route placement. Positions the initial and final sprite bounds just beyond the incoming monitor edge, preserving configured travel distance and holds; steps must not use `untilX` |
| `scenarios.*.actors` | Supporting actors, each with a name, start position, direction, and independent clip steps on the same clock |
| `clips.*.frames.*.hidePrimaryActor` | A supporting actor's combined sprite takes over drawing the Prince, preventing a duplicate character during the embrace |

All `dx`, `anchorX`, `startX`, `untilX`, and `potionX` values are in **DOS movement units**, not PNG pixels. `anchorY` is in native vertical pixels. The room is **140 DOS units / 320 screen pixels** wide. Direction is `1` for right or `-1` for left.

The supplied JSON enables `potionGraphics.alignWithFeet` for both apps. The original potion Y offsets remain **4** for the bottle and **-11** for the bubbles; the override subtracts the bottle's Y offset from both layers, preserving their relative placement. Set the option to `false` to restore the original DOS position. In the tray app, the floor stays at the taskbar edge in either mode; any sprite artwork below that floor can extend into the taskbar area while remaining clipped to the monitor.

Each potion or greeting scenario also has `startDelayTicks`: **24** at the potion scenes' **12 Hz**, and **20** at the greetings' **10 Hz**. This gives the potion or Princess a two-second introduction in both apps before the offscreen Prince starts moving. Set it to `0` to disable the delay, or change the tick count for an individual sequence. The delay uses the scenario's tick rate and follows the sample's speed and pause controls. Potion bubbles continue animating on the elapsed scene clock, while the Prince and Princess's scripted tracks wait together; their later approach and embrace timing stays intact. Restart and replay repeat the delay and reset the bubble phase.

For example, this pose moves forward five DOS units (about 11.43 screen pixels) once, then remains visible for one tick:

```json
{ "frame": 8, "dx": 5, "ticks": 1 }
```

Increasing `ticks` holds a pose longer without repeating its displacement. Movement occurs **before** displaying its pose. At clip exit, `exitDx` happens before `exitFlip`. These distinctions matter for the sliding turn.

Each scenario step selects a clip. `untilX` repeats a looping clip until its root reaches the specified position. `startFrameIndex` chooses the initial pose in that clip; later loops begin at index zero. `exitOnFrames` can delay the transition until a specific pose. The potion routes brake only after running pose 11, as in the game. `repeat` repeats a finite clip; recovery and ending pauses are ordinary configured poses. Running entrances begin directly in `run`, with `startX` far enough outside the stage to hide the entire sprite. The `start` clip supplies acceleration after an onstage stop.

The two potion sequences (**7–8**) use `shortStop`: all eight stopping poses (**53, 54, 55, 56, 49, 50, 51, 52**). Then `shortStand` holds standing pose **15** for **one tick** before the original 21-tick drinking clip, facing the approach direction and mirrored for the left entrance. After drinking, `shortRecover` holds standing pose **15** for **one tick** (about 0.083 seconds). Then `shortStandingTurn` plays poses **45–51**, omitting the final turning pose **52**, and goes immediately into acceleration toward the entry edge. The first turn frame uses `dx: -6, flip: true` because movement precedes a flip, reproducing the original flip followed by a six-unit move in the new direction. These clips and their routes are editable in JSON. Zero root displacement does not imply an identical drawing: the settling poses still change the Prince's body and sprite alignment.

The large red potion uses the original **10 × 15** dungeon vessel (`PRINCE.DAT` resource **163**) and seven **5 × 9** bubble sprites (resources **166–172**) in `src/PopRunner.Core/Assets/Potion`. Its original palette and red bubbles (`#E00030`) are preserved. The bubbles advance once per gameplay tick, pause and step with the Prince, reset on replay, and disappear with the bottle at pickup. The original game randomizes their starting phase; this demo exposes a fixed phase for repeatable playback. Potion graphics are never mirrored with the Prince. The Prince's original drinking poses are shared by small and large potions in the game.

## Extract source artwork

`PopRunner.ExtractAssets` is a .NET 11 console tool, built without Native AOT. With a licensed copy of **DOS Prince of Persia 1.0, 1.1, 1.3, or 1.4**, it can extract all **318** copyrighted images needed by Core for personal recreational use: **251 Prince**, **59 Princess**, and **8 large red potion** images. Replace the example game directory with the location of your licensed copy.

```powershell
dotnet run --project src/PopRunner.ExtractAssets -c Release -- "C:\Games\Prince" "src\PopRunner.Core\Assets"
```

The first argument is the game directory containing **`KID.DAT`, `SHADOW.DAT`, `PV.DAT`, and `PRINCE.DAT`**; the second is the destination **Assets directory**. The tool reads data files directly and does not require or execute `PRINCE.EXE`. The complete Assets directory is ignored by Git.

Output paths follow Core's resource names: `Prince/res401.png` through `res619.png` and `res751.png` through `res782.png`; the 59 expected `Princess/res*.png` names; and `Potion/res163.png`, `Potion/res166-red.png` through `res172-red.png`. PNGs use noninterlaced 8-bit RGBA, preserving the source dimensions, palette colors, transparency, and blank sprites. Existing named outputs are overwritten; unrelated files are preserved. Rebuild and restart PopRunner after regenerating its embedded assets.

The tool checks SHA-256 against the known original data files, accepting both early (1.0/1.1) and later (1.3/1.4) `PRINCE.DAT` hashes. An unknown hash produces a **warning on stderr** and parsing continues. Invalid directories, resource checksums, palettes, image headers, or compressed streams fail with an error. **All images are parsed and all PNGs are prepared in memory before creating an output directory or writing any file.** A parsing failure therefore leaves an existing destination untouched and does not create a new one. A filesystem failure during the subsequent save can still leave partial output. Exit codes are **0** for success (including hash warnings), **1** for extraction/save errors, and **2** for incorrect arguments; `--help` prints usage.

The console tool can extract the complete asset set without System.Drawing, external converters, or NuGet dependencies. See [research/provenance.md](research/provenance.md) for the resource formats and palette references.

To verify against licensed copies of all four supported versions, substitute your game directories in this example:

```powershell
dotnet run --project tests/PopRunner.ExtractAssets.Checks -c Release -- `
  'src/PopRunner.ExtractAssets/bin/Release/net11.0/PopRunner.ExtractAssets.dll' `
  'src/PopRunner.Core/Assets' 'artifacts/extractor-checks' `
  'C:\Games\Prince-1.0' `
  'C:\Games\Prince-1.1' `
  'C:\Games\Prince-1.3' `
  'C:\Games\Prince-1.4'
```

This check project builds the extractor, compares all 318 output names, dimensions and exact RGBA pixels (including RGB beneath transparent pixels) against your locally generated baseline assets, and requires generated PNG files to be byte-identical across versions. It also verifies a valid unknown-hash warning and that a malformed final image or truncated compressed image leaves existing output untouched and creates no new destination. A four-version run performs **1,590 image comparisons**, including the modified-hash input. Test inputs and outputs are kept in a unique run folder beneath the third argument; the licensed game files and baseline assets are read-only inputs.

For sequence **5**, **Greet the Princess**, the Prince starts offscreen at 152 DOS units, already running left toward her starting position of 78. Her `princessWait` clip holds for 10 ticks, then `princessReunion` plays the original turning, reaching, and embrace poses. The extra waiting tick preserves contact timing after extending his entrance. Config sprite IDs **1020–1033** map to Princess cutscene poses **20–33** (`res903.png` through `res916.png`). At pose 1028, `hidePrimaryActor` hides the separate Prince because `res911.png` already includes him. Both tracks retain their final pose until the scenario finishes. The final embrace hold is the last frame's `ticks` value; adjust it along with the Prince's `greeting` hold if desired. Supporting actors reset along with the Prince on replay, selection changes, and reload.

Sequence **6**, **Greet the Princess (from the left)**, mirrors that greeting. Both actors face right, their starting positions and the Prince's approach target are reflected across the room, and the renderer flips the existing sprites, including the combined embrace. It reuses the same clips, timings, and artwork; no additional assets are required. Both greeting variants finish at the embrace. The tray places this variant's Princess about one-third of the monitor width from the left edge.

Sequences **9–10** share `carefulStep`, `carefulHold`, and `carefulStepBack`. The forward step uses original poses **121–132** and moves **11 DOS units**. The hold's `clips.carefulHold.frames[0].ticks` is **48**, giving four seconds at the scenes' **12 Hz** clock. The backward clip reverses those poses and retraces their positions while keeping the same facing direction. `desktopEdgeEntry: true` places both ends about one original image pixel outside the monitor, so the single step brings the Prince fully into view before he backs offscreen. Change the clip frame timings or hold duration in the JSON to tune them.

JSON image identifiers such as `Assets/Prince/res401.png` remain logical paths relative to `src/PopRunner.Core`. A local build of Core embeds `animation.json` and the locally generated sprites and resolves them to resource streams. The Windows Forms project renders the images with `System.Drawing`; Core has no `System.Drawing` dependency. A local published app needs no loose `Assets` directory or `animation.json`. The embedded artwork remains copyrighted; see the third-party notices before sharing a build.

## Original-game alignment and timing

The defaults use the DOS v1.4 animation reference: its complete **241 × 5-byte frame table** matches SDLPoP's DOS-derived table byte for byte. The 219 Prince resources in `KID.DAT` supply their original image dimensions. Reference executable hashes and offsets are documented in [research/provenance.md](research/provenance.md).

- Normal movement uses a 60 Hz timer with a divider of **5**, giving **12 simulation ticks per second**. The reference executable contains that divider. A Windows Forms timer wakes the UI; a `Stopwatch` and separate fixed-timestep accumulator determine simulation ticks, so timer scheduling does not set the animation speed.
- The original eight-pose run cycle advances **26 DOS units**: about **59.43 native screen pixels per cycle**, or **89.14 pixels/second** at normal speed. Movement is stepped per pose; it is not interpolated.
- Original horizontal conversion is `2 × 320/280 = 16/7` pixels per movement unit. Treating the sequence deltas as PNG pixels makes the Prince too slow.
- Sprite positions use the original horizontal/vertical offsets and even/odd-pixel adjustment. Right-facing images are mirrored against their right edge. Cropped PNG widths do not determine the root position.
- The reversal displays poses **53–65**, applies the original **−14-unit** origin adjustment, flips facing, and enters running pose **13**.
- Drinking uses the original **21-tick** drink script, including the repeated pose **205** and the return through **201 / 198**. The drink script's initial **+4** and final **−4** origin adjustments are retained.
- The greeting retains the ending's Princess positions, poses, movement, and contact alignment, with an extended offscreen Prince entrance. Its **10 Hz** clock comes from the cutscene's 60 Hz timer divided by **6**; ordinary running scenarios keep their **12 Hz** clock. The Princess cutscene frame table also matches all **86 records** in the reference executable byte for byte. The demo holds the final embrace before cycling.

This is an animation demo with newly drawn background scenery. The timing defaults follow the original data; they were not calibrated by recording an interactive DOSBox session. All relevant values remain editable for further tuning.

See [research/provenance.md](research/provenance.md) for exact offsets and primary references. To tune the animations, edit `src/PopRunner.Core/animation.json`, then rebuild and restart the app.

## Verify and publish

```powershell
dotnet build PopRunner.slnx -c Release
dotnet run --project tests/PopRunner.Checks -c Release
dotnet publish src/PopRunner.Sample -c Release --self-contained false -o artifacts/demo
```

The dependency-free Core checks use small synthetic configurations to test frame holds, movement and flip ordering, repetition, travel targets, supporting actors, potion events, placement, clock behavior, JSON parsing, and validation. Resource checks inspect the embedded images and verify that the packaged configuration loads and validates. The suite reports named test cases rather than counting every assertion in a loop. It does not copy the choreography or store trace snapshots from `animation.json`, so editing the configured poses or timings does not require updating a second set of animation data in the tests.

The interface is a standard `System.Windows.Forms.Form` with menus, a `ComboBox`, `Button`, `NumericUpDown`, `CheckBox`, and `StatusStrip` controls. Sprite rendering uses `System.Drawing` with nearest-neighbor scaling. The animation engine and embedded-resource access remain in Core, independent of the UI framework. Resource checks read the packaged configuration and image streams from that same assembly.

Core uses source-generated `System.Text.Json` metadata and enables AOT compatibility analyzers. Its JSON loader retains support for comments, trailing commas, and case-insensitive property names, while rejecting unknown fields. To compile and run the full check suite with Native AOT on Windows x64:

```powershell
dotnet publish tests/PopRunner.Checks -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot/checks
artifacts\aot\checks\PopRunner.Checks.exe
```

The AOT check project keeps all of Core reachable so unused methods are analyzed too. A Windows Native AOT compiler toolchain is required. The Windows Forms sample uses its normal managed build.

For repeatable visual QA, run the actual Windows Forms app in capture mode:

```powershell
artifacts\demo\PopRunner.Sample.exe --capture-preview artifacts\previews\preview.png
```

It checks the playback controls, stepping, potion bubble advancement/reset, scenario selection, speed, guides, embedded-default loading, and the read-only JSON viewer; checks greeting selection/restart, potion shortcuts, clock changes, and cycling **5 → 6 → 7 → 8 → 9 → 10 → 1**; captures all configured scenarios using the controls' real paint paths, plus potion drinking, both greetings' approach/contact/embrace, the potion sequences' turns and departures, and both careful steps' entry/hold/retreat; captures the minimum window with alignment guides; writes `ui-smoke-checks.txt`; and exits. This capture workflow uses the embedded configuration.
