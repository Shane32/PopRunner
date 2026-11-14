# Animation reference

The sequence order below is shared by the sample's selector and keyboard shortcuts and the tray's **Play** menu. All ten sequences are in the supplied random selection pool. The authoritative routes, poses, and timings are in [animation.json](../src/PopRunner.Core/animation.json).

| Number | Sequence | Entry and ending |
| --- | --- | --- |
| 1 | Run to the right | Run in from the left and leave through the right edge. |
| 2 | Run to the left | Run in from the right and leave through the left edge. |
| 3 | Slide and return left | Run in from the left, slide into a reversal, and run back left. |
| 4 | Slide and return right | Run in from the right, slide into a reversal, and run back right. |
| 5 | Greet the Princess | Run in from the right and finish at the embrace. |
| 6 | Greet the Princess from the left | Mirror sequence 5 and finish at the embrace. |
| 7 | Quick potion stop and return right | Run in from the right, stop, drink facing left, turn, and run back right. |
| 8 | Quick potion stop and return left | Run in from the left, stop, drink facing right, turn, and run back left. |
| 9 | Careful step in from the left | Step right into view, stand for four seconds, and step backward off the left edge. |
| 10 | Careful step in from the right | Step left into view, stand for four seconds, and step backward off the right edge. |

Running entrances start fully offscreen in the running cycle. Potion and greeting scenes show the potion or Princess for two seconds before the actor scripts start: `startDelayTicks` is 24 at 12 Hz for potions and 20 at 10 Hz for greetings. Potion bubbles keep animating during this introduction. Replay repeats the delay and resets the actors and bubble phase.

## Desktop placement

The sample uses a fixed room. The tray adapts a copy of each route to the primary monitor, preserving per-pose movement and tick rates. Slides, potions, and the Princess sit about one-third of the monitor width from the entry edge. Longer approaches add complete running cycles, with a matching extension to the Princess's wait so contact stays synchronized.

The tray's floor baseline meets the taskbar and its drawing window clips at the monitor edges. Careful-step routes receive only an edge translation, preserving their short travel distance. See [DesktopScenarioLayout.cs](../src/PopRunner/DesktopScenarioLayout.cs) for the placement logic and [configuration.md](configuration.md) for adjustable settings.

## Princess greetings: 5 and 6

Sequence 5 starts the Prince at root X 152, facing left, and the Princess at X 78. After the introductory delay, `princessWait` holds her initial pose for ten ticks before `princessReunion`. This wait preserves the reunion contact timing with the offscreen entrance.

Princess configuration IDs 1020–1033 represent cutscene poses 20–33. At pose 1028, `hidePrimaryActor` hides the separate Prince: the Princess's sprite already contains both characters. The two tracks retain their final poses until the scenario finishes. The final combined sprite holds for 33 ticks; the Prince's `greeting` clip has a separate 32-tick standing hold.

Sequence 6 reflects the coordinates across the 140-unit room: the Prince starts at X −12, the Princess at X 62, and both face right. It shares the same clips, timing, and images, including the combined embrace; rendering supplies the mirroring. Both greetings end at the embrace without a departure.

## Potion stops: 7 and 8

Both routes use the large red potion and the same clip chain:

```text
run → shortStop → shortStand → drink → shortRecover → shortStandingTurn → start → run → end
```

The incoming run may finish on pose **7 or 11**, as specified by `exitOnFrames`. `shortStop` retains all eight stopping poses: 53, 54, 55, 56, 49, 50, 51, and 52, with nine DOS units of net braking movement. `shortStand` then holds standing pose 15 for one tick before drinking.

The 21-tick `drink` clip retains the original +4-unit entry and −4-unit exit adjustments, repeated pose 205, and return through poses 201 and 198. It hides the bottle and bubbles when the Prince picks up the potion and records consumption at the configured drinking pose. Sequence 7 drinks facing left; sequence 8 mirrors it, drinking facing right.

Afterward, `shortRecover` holds standing pose 15 for one tick, about 0.083 seconds. `shortStandingTurn` plays poses 45–51, omitting the original final turn pose 52, then enters acceleration without another standing hold. Its first command is `dx: -6, flip: true`: movement in the old direction precedes the flip, yielding the original position in the new direction. The acceleration clip itself retains its original poses, including those with zero root displacement. A zero displacement can still change the visible pose or alignment.

The supplied potion alignment override raises both bottle and bubbles together so the vessel meets the feet baseline. Disabling `potionGraphics.alignWithFeet` restores the original offsets, including the bottle extending four native pixels below that baseline.

## Careful steps: 9 and 10

These routes share `carefulStep`, `carefulHold`, and `carefulStepBack`. The forward step uses poses 121–132 for one tick each and advances 11 DOS units. Standing pose 15 then holds for 48 ticks at 12 Hz, giving four seconds.

The return reverses poses 132–121 and retraces their root positions without changing facing, then returns to standing. Its movement commands undo the next forward pose's entry movement, because the engine moves before displaying a pose. Simply reversing and negating each same-pose displacement would not reproduce those positions. This rewind is demo choreography, rather than the original game's standing-sprite `stepback` sequence.

With `desktopEdgeEntry` enabled, the tray places the initial and final sprite bounds about one original image pixel outside the incoming edge. The single step brings the Prince fully into view for the hold, then takes him back offscreen. The sample also starts these routes just beyond its room edges.

## DOS alignment and timing

Ordinary scenes use **12 simulation ticks per second**, corresponding to the game's 60 Hz timer divided by five. Greetings use the ending cutscene's **10 Hz** clock, a divisor of six. UI timer wake-ups and monitor refresh do not determine animation speed; a fixed-step accumulator advances the simulation.

The eight-pose run advances **26 DOS units** per cycle: about 59.43 native screen pixels, or 89.14 pixels per second at 12 Hz. Movement occurs once on entry to a pose, before it is drawn; holding that pose longer does not repeat its displacement. Motion is stepped rather than interpolated. At clip exit, `exitDx` precedes `exitFlip`. Sliding reversals use poses 53–65, the original −14-unit exit adjustment, and then running pose 13.

Horizontal movement uses **16/7 native screen pixels per DOS unit**, making a room 140 units / 320 pixels wide. Sprite offsets, integer truncation, and even/odd-pixel alignment determine placement; cropped PNG widths do not determine the actor's root. Right-facing sprites mirror against their right edge. Mirrored routes can differ by one rendered pixel because of truncation and parity. The default vertical pixel correction is 1.2 to approximate DOS display proportions.

The animation data follows the DOS reference; these defaults were not calibrated by recording an interactive DOSBox session. The sample's background and floor are demo scenery drawn in code. For implementation details, see [AnimationEngine.cs](../src/PopRunner.Core/AnimationEngine.cs); for source definitions, verification, executable offsets, and hashes, see [research/provenance.md](../research/provenance.md).
