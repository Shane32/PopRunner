# Configuration reference

Edit [animation.json](../src/PopRunner.Core/animation.json), then rebuild and restart the app. Core embeds this file and the generated PNGs; neither app reads loose configuration or assets at runtime. The sample's **F5 / Load defaults** command reloads the configuration embedded in its current build, and **View JSON** shows that same file read-only.

For setup and everyday use, see the [README](../README.md). For the supplied routes, see the [animation reference](animation-reference.md).

## Defaults and JSON syntax

[AnimationConfig.cs](../src/PopRunner.Core/AnimationConfig.cs) defines the model, defaults, and validation. The supplied JSON omits properties and entire sections that equal those defaults. Add a property to override it; if a C# default changes, omitted values follow that new default.

Comments, trailing commas, and case-insensitive property names are accepted. Unknown fields are rejected. Invalid references, timings, and routes are rejected before replacing the sample's active configuration.

Three entry types accept shorthand. Each shorthand supplies the same defaults as an object with only its identifying property:

| Entry | Shorthand | Equivalent object |
| --- | --- | --- |
| Sprite value | `"res407.png"` | `{ "image": "res407.png" }` |
| Clip frame | `15` | `{ "frame": 15 }` |
| Sequence step | `"end"` | `{ "clip": "end" }` |

Use an object when supplying additional settings, such as `{ "frame": 8, "dx": 5 }` or `{ "clip": "run", "untilX": 120 }`. Step shorthand also works for supporting actors. In the supplied JSON, each sprite, frame, and step object occupies one line.

Sprite filenames resolve to unique embedded images in the `Prince`, `Princess`, and `Potion` folders. Full logical paths such as `Assets/Prince/res407.png` also work. A bare filename must be unambiguous; a full path identifies a particular folder. [AnimationResources.cs](../src/PopRunner.Core/AnimationResources.cs) implements resource lookup.

## Desktop presentation

These settings apply to the tray app. Its monitor-adapted routes are copies of the configured scenes, leaving the sample's routes intact.

| Property under `desktop` | Default | Meaning |
| --- | --- | --- |
| `spriteScale` | `2` | Source-pixel size at 96 DPI, multiplied by the primary monitor's DPI scale. |
| `taskbarGapPixels` | `0` | Physical screen-pixel gap between the floor baseline and the top of the primary taskbar. |
| `actionEdgeFraction` | `1/3` | Distance of the slide, potion, or Princess from the incoming edge, as a fraction of monitor width. |
| `randomScenarioIds` | All ten scene IDs | Eligible scenes for double-click, Play Random, and automatic playback. Entries must be unique and refer to existing scenes. |
| `timerIntervalMilliseconds` | `15` | Win32 timer wake-up interval; animation ticks are controlled separately. |

The **Automatic Trigger** menu selection is stored separately in the user's registry. See [automatic playback](implementation.md#automatic-playback) for the probabilities, activity checks, and registry values.

## Clock and preview stage

| Property | Default | Meaning |
| --- | --- | --- |
| `clock.ticksPerSecond` | `12` | Default fixed simulation rate. A scenario can override it. |
| `clock.maxCatchUpSeconds` | `0.5` | Maximum elapsed time processed after a UI stall, before applying playback speed. |
| `stage.width`, `stage.height` | `320`, `100` | Native pixel dimensions of the sample's stage. |
| `stage.floorY` | `78` | Actor floor baseline in native vertical pixels. |
| `stage.pixelAspectY` | `1.2` | Vertical pixel correction: `1.2` approximates a DOS CRT; `1` uses square PNG pixels. Also used by the tray renderer. |
| `stage.movementUnitPixels` | `16/7` | Horizontal screen pixels per DOS movement unit. |
| `stage.preStretchPixelSize` | `8/7` | One original horizontal pixel after stretching, used for the parity adjustment. |
| `stage.quantizeToPixels` | `true` | Truncate the rendered anchor position toward zero using the original integer behavior. |
| `stage.showGuides` | `false` | Initial sample alignment-guide setting. |
| `stage.autoAdvance` | `true` | Initial sample cycling setting. |

The fractions above describe the C# defaults. To override them in JSON, use a JSON number, such as `0.3333333333333333`; JSON does not accept expressions like `1/3`.

## Sprites

`sprites` maps pose IDs to filenames or objects. The IDs follow the original frame table and are independent of image resource numbers. The supplied configuration also uses separate ID ranges for Princess and potion sprites.

| Sprite object property | Default | Meaning |
| --- | --- | --- |
| `image` | Empty; must resolve to an image | Unique PNG filename or full logical resource path. |
| `anchorX` | `0` | Horizontal frame offset in DOS movement units, applied in the facing direction. |
| `anchorY` | `0` | Vertical frame offset in native pixels. |
| `oddPixel` | `false` | Original horizontal pixel-parity flag. Together with facing, selects a one-pixel alignment correction before stretching. |

Placement uses the actor's root, frame offsets, facing, and parity. It does not derive the root from the cropped PNG's width. See [GetPlacement](../src/PopRunner.Core/AnimationEngine.cs) for the calculation.

## Clips and frames

`clips` maps names to reusable animation clips.

| Clip property | Default | Meaning |
| --- | --- | --- |
| `label` | Empty | Human-readable phase label in the sample. |
| `frames` | Empty; must contain frames | Ordered frame numbers or frame objects. |
| `loop` | `false` | Marks a clip as eligible for an `untilX` travel step. |
| `exitDx` | `0` | Movement in the old facing direction when the clip exits. |
| `exitFlip` | `false` | Flip facing after applying `exitDx`. |

| Frame object property | Default | Meaning |
| --- | --- | --- |
| `frame` | `0`; must refer to a sprite | Pose ID. |
| `ticks` | `1` | How many simulation ticks to display this pose. |
| `dx` | `0` | Displacement in DOS movement units, applied once before the pose is displayed. |
| `flip` | `false` | Flip facing after the frame's movement. |
| `hidePotion` | `false` | Hide the potion and bubbles for the rest of this playback. |
| `consumePotion` | `false` | Record potion consumption for the rest of this playback. |
| `hidePrimaryActor` | `false` | Hide the separate Prince when a supporting actor's combined sprite replaces him. |

For example, `{ "frame": 8, "dx": 5, "ticks": 2 }` moves five DOS units once, then holds the pose for two ticks. Increasing `ticks` extends the hold without repeating movement. At normal speed, two ticks at 12 Hz last about 0.167 seconds.

## Sequences and supporting actors

Each entry in `scenarios` defines one sequence. Its array order supplies the displayed sequence number; `id` identifies it for random selection and other references.

| Scenario property | Default | Meaning |
| --- | --- | --- |
| `id` | Empty | Unique identifier, used by `desktop.randomScenarioIds`. |
| `title`, `subtitle`, `route` | Empty | Menu/selector title, sample description, and route description. |
| `direction` | `1` | Initial facing: `1` right, `-1` left. |
| `startX` | `0` | Initial root position in DOS movement units. |
| `hasPotion` | `false` | Show the configured potion for this sequence. |
| `potionX` | `0` | Potion root position in DOS movement units. |
| `ticksPerSecond` | `null` | Use the global clock unless specified. Greetings explicitly use `10`. |
| `startDelayTicks` | `0` | Initial delay, from 0 to 10000 scenario ticks, before actor scripts begin. |
| `desktopEdgeEntry` | `false` | Place a finite step-and-return route just outside the incoming monitor edge in the tray app. Its steps must not use `untilX`. |
| `steps` | Empty; must contain steps | Ordered clip names or step objects for the Prince. |
| `actors` | Empty | Supporting actors with their own step lists on the same scenario clock. |

A supporting actor has `name`, `startX`, `direction`, and `steps`. Its default direction is **`-1`**, unlike the Prince's default of `1`. Actors wait together during the initial delay and retain their final poses while the remaining tracks finish.

| Step object property | Default | Meaning |
| --- | --- | --- |
| `clip` | Empty; must refer to a clip | Clip to play. |
| `startFrameIndex` | `0` | Zero-based initial frame index; later repetitions start at index zero. |
| `repeat` | `1` | Number of clip repetitions when there is no travel target. |
| `untilX` | `null` | Repeat a looping clip until the root reaches this position in the facing direction. |
| `exitOnFrames` | `[]` | Optional pose IDs at which a travel step may transition after reaching its target. |

The potion scenes specify `startDelayTicks: 24` at 12 Hz, and greetings specify `20` at 10 Hz: both give a two-second introduction. Set the count to `0` to start immediately. Potion bubbles animate during the delay while actor scripts wait. The sample's pause and speed controls apply to this delay, and replay resets it.

## Potion layers

`potionGraphics` specifies separate bottle and bubble layers. It is required when any scenario has a potion.

| Property | Default | Meaning |
| --- | --- | --- |
| `bodyFrame` | `0`; must refer to a sprite | Bottle pose ID. |
| `bubbleFrames` | Empty; must contain sprite IDs | Ordered bubble poses. |
| `ticksPerFrame` | `1` | Hold duration for each bubble pose in scenario ticks. |
| `initialBubbleFrame` | `0` | Zero-based initial bubble index. |
| `bodyOffsetX`, `bubbleOffsetX` | `0` | Native-pixel horizontal offsets from the potion root. |
| `bodyOffsetY`, `bubbleOffsetY` | `0` | Position each layer's exclusive bottom relative to `floorY + 1`. |
| `alignWithFeet` | `false` | Shift both layers vertically so the bottle bottom meets the feet baseline. |

The supplied JSON enables `alignWithFeet` and preserves the original Y offsets: `4` for the bottle and `-11` for bubbles. The override subtracts the bottle's Y offset from both layers. Setting it to `false` restores the original placement without changing those offsets. The tray floor remains at the taskbar edge in either mode; artwork below the floor may extend into the taskbar area while staying clipped to the monitor.

Bubbles use the scenario tick clock, pause and step with the animation, reset on replay, and disappear when the potion is hidden. The fixed initial phase makes playback reproducible. Potion layers are not mirrored with the Prince.

## Movement and timing rules

Horizontal `dx`, `exitDx`, `anchorX`, `startX`, `untilX`, and `potionX` values use **DOS movement units**. The default conversion is `16/7` native screen pixels per unit; the room is 140 DOS units / 320 screen pixels wide. Vertical anchors and potion-layer offsets use native pixels.

Frame movement happens before displaying the frame and before its `flip`. Clip-exit movement happens before `exitFlip`. An `untilX` transition waits for the current pose's hold to finish and, when specified, an allowed `exitOnFrames` pose. A pose with no root movement can still change the drawing and its alignment.

Running entrances start in the `run` loop outside the visible area. The `start` clip supplies acceleration after an onstage stop. The careful-step sequences instead use finite routes and the desktop edge-placement option. See the [animation reference](animation-reference.md) for the supplied clips, and [research/provenance.md](../research/provenance.md) for original-game timing and alignment sources.
