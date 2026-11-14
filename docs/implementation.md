# Implementation details

Build and usage instructions are in the [README](../README.md). These notes describe how the apps load assets, run the animation engine, and interact with Windows. The original game's data formats, animation tables, timing references, and source attribution are documented in [research/provenance.md](../research/provenance.md).

## Project boundaries

| Project | Responsibility |
| --- | --- |
| [PopRunner.Core](../src/PopRunner.Core) | Configuration, embedded resources, fixed-tick animation engine, supporting actor tracks, and sprite placement. It has no UI framework or `System.Drawing` dependency. |
| [PopRunner](../src/PopRunner) | Native Win32 tray icon, menus, automatic playback policy, primary-monitor route adaptation, PNG decoding, and transparent desktop rendering. |
| [PopRunner.Sample](../src/PopRunner.Sample) | Windows Forms controls, configuration viewer, preview scenery, and GDI+ rendering using the shared Core engine. |
| [PopRunner.ExtractAssets](../src/PopRunner.ExtractAssets) | Standalone managed console program that prepares the required PNGs from a licensed DOS game copy. It builds independently of Core and its assets. |
| [PopRunner.Checks](../tests/PopRunner.Checks) | Dependency-free Core behavior checks using synthetic configurations, plus embedded configuration/resource checks. |
| [PopRunner.ExtractAssets.Checks](../tests/PopRunner.ExtractAssets.Checks) | Extractor comparison and failure-path checks using caller-supplied game copies and baseline images. |

[Program.cs](../src/PopRunner/Program.cs) loads the configuration and creates the overlay, playback selector, automatic trigger, and tray host. A session-local named mutex prevents a second normal tray instance in the same Windows session. The tray has an ordinary hidden top-level owner window, allowing it to receive Explorer's `TaskbarCreated` broadcast and restore its notification icon after Explorer restarts.

Both apps advance [AnimationEngine](../src/PopRunner.Core/AnimationEngine.cs) through the same `AnimationClock`. A timer wakes the UI, while a `Stopwatch` supplies elapsed time to the fixed-step accumulator. The timer interval does not set the simulation rate. Each scenario can override the default rate; the supplied running scenes use 12 ticks per second and greetings use 10. The clock caps elapsed-time catch-up after a UI stall. Movement occurs on configured pose changes rather than being interpolated between ticks.

## Embedded resources and JSON

[PopRunner.Core.csproj](../src/PopRunner.Core/PopRunner.Core.csproj) embeds `animation.json` and every local PNG under `Assets`. [AnimationResources](../src/PopRunner.Core/AnimationResources.cs) exposes these as streams. Logical image paths such as `Assets/Prince/res407.png` identify resources inside the assembly; they are not files opened from the current directory. A unique bare filename is also accepted. Ambiguous filenames require their full logical path. The physical `Prince`, `Princess`, and `Potion` source folders remain separate.

[AnimationConfig.Load](../src/PopRunner.Core/AnimationConfig.cs) deserializes the JSON, resolves image filenames to full logical paths, and validates the result before returning it. Its [source-generated JSON metadata](../src/PopRunner.Core/AnimationJsonContext.cs) accepts comments, trailing commas, and case-insensitive property names, and rejects unknown fields. Omitted properties retain their C# defaults.

[AnimationEntryJsonConverters](../src/PopRunner.Core/AnimationEntryJsonConverters.cs) use C# type unions to accept sprite objects or image strings, frame objects or integer pose IDs, and step objects or clip-name strings. The converters expand shorthand at the loading boundary, using source-generated metadata for object alternatives. The engine and callers receive the existing sprite, frame, and step objects, so playback does not repeatedly inspect JSON alternatives. This includes supporting actors' step lists.

Editing the source JSON or images requires rebuilding and restarting to replace the embedded resources. The sample's **Load defaults** command reloads resources from the running assembly; it does not reread the repository files. Its **View JSON** command shows the embedded text read-only. Published apps need neither loose PNGs nor a loose `animation.json`. Embedded game artwork retains its copyright restrictions; see [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

### PNG lifetime in the tray app

The [DesktopOverlay constructor](../src/PopRunner/DesktopOverlay.cs) opens and decodes every sprite referenced by the loaded configuration when the program starts. [PngImage.Load](../src/PopRunner/PngImage.cs) validates PNG chunks and CRCs, decompresses scanlines with `ZLibStream`, reconstructs their filters, and converts the pixels to top-down premultiplied BGRA. Each `PngImage` holds its dimensions and a managed `byte[]`. The overlay retains these in an in-memory dictionary keyed by frame ID.

Starting a sequence creates its playback state and reuses those decoded pixels. Drawing a frame also reuses them; it does not decompress the PNG again. Scaled copies are not cached. Scaling, optional mirroring, clipping, and blending occur as pixels are written into the window's drawing buffer.

The tray icon is prepared separately by [TrayHost.CreatePrinceIcon](../src/PopRunner/TrayHost.cs), using `Assets/Prince/res407.png`. It decodes that image once at startup, finds the visible bounds, scales and centers the cropped sprite in Windows' small-icon dimensions, preserves the taller DOS pixel aspect, and mirrors the Prince to face right. `CreateIconIndirect` produces the `HICON` supplied to `Shell_NotifyIcon`.

### PNG lifetime in the sample

[SpriteAtlas](../src/PopRunner.Sample/SpriteAtlas.cs) decodes the configured sprites when the sample loads its configuration. It creates detached `Bitmap` copies and caches both normal and mirrored versions. Temporary PNG streams and source bitmaps are then disposed. Reloading defaults prepares a replacement configuration and atlas before replacing the active state, then disposes the old atlas.

[StageView](../src/PopRunner.Sample/StageView.cs) draws a native-size scene into a bitmap and scales that scene to the control with nearest-neighbor sampling and the configured pixel aspect correction. The dark background, arches, pillars, and tiled floor are drawn with GDI+ shapes and lines in `DrawScene`; they are demo scenery rather than extracted game images. This scenery is cleared and redrawn with the sprites on each paint.

## Desktop rendering

[DesktopOverlay](../src/PopRunner/DesktopOverlay.cs) owns a topmost Win32 layered popup with transparent, tool-window, and no-activate extended styles. It also returns `HTTRANSPARENT` for hit testing and `MA_NOACTIVATE` for mouse activation. Mouse input passes through the entire overlay, including opaque sprite pixels, to windows underneath. The overlay is hidden when playback finishes.

[LayeredSurface](../src/PopRunner/LayeredSurface.cs) allocates a memory device context and a `CreateDIBSection` bitmap. The bitmap is top-down, uncompressed, and 32 bits per pixel, with four bytes per pixel and a `width * 4` row stride. Its BGR byte ordering follows the Windows `BI_RGB` bitmap format. Presentation sets `AC_SRC_ALPHA`, using the fourth byte as premultiplied alpha.

Each render clears the DIB to transparent pixels, draws the visible potion layers, Prince, and supporting actors, and calls `UpdateLayeredWindow`. The drawing code maps destination pixels to source pixels using `floor(position / scale)` for nearest-neighbor sampling. Mirroring reverses the sampled source X coordinate. Drawing clips to the buffer dimensions, so nothing extends onto an adjacent monitor.

The source-over blend combines overlapping sprites in the current render:

```text
result = premultiplied source + destination * (1 - source alpha)
```

Transparent source pixels leave the destination unchanged; opaque pixels replace it directly. Partial alpha blends with sprites already drawn into that buffer. Windows then composites the completed buffer over the desktop. No pixels from the previous animation frame remain in the cleared drawing buffer.

### Monitor and DPI changes

The tray project's [embedded manifest](../src/PopRunner/app.manifest), selected by `ApplicationManifest` in its [project file](../src/PopRunner/PopRunner.csproj), declares `PerMonitorV2` awareness. [DesktopOverlay.UpdateLayout](../src/PopRunner/DesktopOverlay.cs) reads the primary monitor's monitor/work rectangles and obtains the overlay window's DPI with `GetDpiForWindow`. Horizontal scale is `desktop.spriteScale * dpi / 96` with a minimum of 0.5; vertical scale additionally applies `stage.pixelAspectY`.

The overlay spans the primary monitor's width. Its floor baseline is the work-area bottom minus `desktop.taskbarGapPixels`, constrained to the monitor. It reserves enough height for configured sprite extents above and below the floor, including potion offsets. Below-floor artwork can extend into the taskbar region without raising the feet baseline, while the window remains within the monitor bounds.

Layout is recalculated before every sequence and when the overlay receives `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, or `WM_SETTINGCHANGE`. If moving to a newly primary monitor changes the window's DPI during positioning, it recalculates again using the new DPI. A layout change during playback restarts that sequence with a newly adapted route and redraws it immediately. The DIB is replaced when its required dimensions change.

### Adapting routes to the desktop

[DesktopScenarioLayout](../src/PopRunner/DesktopScenarioLayout.cs) clones the selected scenario and its actor tracks before adapting them to the monitor. It translates slide, potion, and Princess interactions toward the configured fraction of monitor width from the entry edge, constrained by the interaction's visible bounds. Running entrances extend by whole run cycles to begin offscreen, and departure targets extend beyond the monitor. Supporting actors' initial waits are adjusted to preserve the greeting handoff after the longer approach. The source configuration and sample routes are unchanged.

Finite `desktopEdgeEntry` routes use their configured movement and durations. The adapter translates their initial and final bounds just beyond the entry edge and requires their stationary hold to fit on the monitor. This lets the careful-step scenes enter and retreat through the same edge without inserting run cycles or changing their facing.

The engine applies potion alignment in native scene coordinates before desktop scaling. When `potionGraphics.alignWithFeet` is enabled, the bottle's original Y offset is subtracted from both bottle and bubbles. Disabling it preserves the original offsets and their relative placement. Both modes keep the desktop floor at the taskbar edge.

## Automatic playback

[TrayHost](../src/PopRunner/TrayHost.cs) starts a 60,000 ms Win32 timer when automatic playback is enabled. Selecting another frequency persists the preference, updates the radio menu item, and replaces that timer; the next check is one minute later. Disabled mode removes the timer. Manual menu selection and tray activation use [DesktopPlayback](../src/PopRunner/DesktopPlayback.cs); automatic playback uses the same configured random selection pool.

[AutomaticTrigger.OnMinute](../src/PopRunner/AutomaticTrigger.cs) first rejects disabled mode, an already playing animation, or an ineligible activity state. Each eligible minute then makes one independent random draw:

| Frequency | Chance per eligible minute | Usage assumption |
| --- | --- | --- |
| Hourly | 1/60 | One hour |
| Daily | 1/480 | Eight hours per day |
| Weekly | 1/2400 | Forty hours per week |
| Disabled | No draw | Manual playback only |

The daily and weekly assumptions are private class-level constants in `AutomaticTrigger`. A successful draw plays one random sequence. These are statistical averages, not scheduled appointments: successful draws can be close together or far apart. Locked, idle, busy, and missed minutes do not accumulate for a later catch-up.

### Activity gates

[DesktopActivity.IsEligible](../src/PopRunner/DesktopActivity.cs) requires all of these checks to pass immediately before the draw:

1. **Active and unlocked session.** `WTSQuerySessionInformationW` requests extended information for the process's own session, whether local or RDP. It requires the expected level-1 structure, `WTSActive` connection state, and `WTS_SESSIONSTATE_UNLOCK` flag. Locked, disconnected, unknown, failed, and invalid responses suppress the attempt. It frees the Windows-owned result buffer in `finally`.
2. **Recent input.** `GetLastInputInfo` must report keyboard or mouse input within the last five minutes in that same session. The comparison uses unsigned subtraction against the 32-bit uptime counter so the counter wrapping does not invalidate the interval.
3. **No recorded microphone activity.** The user and machine `Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone` trees are read recursively, including packaged applications and `NonPackaged` descendants. A positive `LastUsedTimeStart` requires a positive `LastUsedTimeStop` at least as recent as that start. Missing or zero start values represent no recorded use. Malformed values, unreadable keys, disappearing children, or excessive tree depth suppress the attempt.

The microphone registry is Windows privacy history, not a documented live ownership API. Programs bypassing that tracking can be missed, and a crashed program can leave an apparently active record that suppresses playback. A missing store means no recorded use; an access/read error suppresses playback. The app retries the eligibility check at the next minute rather than displaying a warning for each rejected attempt.

### Registry preference

The frequency is a DWORD named `AutomaticTrigger` under `HKEY_CURRENT_USER\Software\PopRunner`:

| Value | Frequency |
| --- | --- |
| 0 | Disabled |
| 1 | Hourly |
| 2 | Daily |
| 3 | Weekly |

Missing, invalid, or unreadable preferences default to **Weekly**. `SetFrequency` writes the registry successfully before changing the active policy. The native checks inject deterministic activity and random callbacks and use temporary preference keys, preserving the user's selection.

## Native AOT

All projects target .NET 11, using the SDK selected by [global.json](../global.json). The [tray project](../src/PopRunner/PopRunner.csproj) publishes for Windows x64 with Native AOT and explicitly enables AOT and trimming analyzers. [Core](../src/PopRunner.Core/PopRunner.Core.csproj) declares `IsAotCompatible`, so compatibility analysis also applies to the shared engine. Native publishes use the runtime/compiler patch selected by the installed SDK; updating that SDK and republishing incorporates its newer patches.

The tray app uses Win32 `LibraryImport` declarations and unmanaged window callbacks. PNG processing uses managed buffers and `ZLibStream`; GDI DIBs and `UpdateLayeredWindow` present the result. It has no Windows Forms, WPF, `System.Drawing`, or COM dependency. A published native executable requires no installed .NET runtime. The Windows Forms sample and asset extractor use ordinary managed builds.

JSON object conversion explicitly uses the [generated metadata context](../src/PopRunner.Core/AnimationJsonContext.cs), including the object alternatives handled by the union converters. The [AOT Core check project](../tests/PopRunner.Checks/PopRunner.Checks.csproj) roots the entire Core assembly when published with Native AOT, keeping otherwise unused methods reachable for analysis.

Production builds omit native self-test code. Setting `EnableNativeChecks=true` defines `POP_RUNNER_CHECKS` independently of Debug/Release and includes the checks, diagnostics, and helper imports. Omitting it, or setting it to false, excludes those paths and the `--self-test` entry point. See the [native tray testing instructions](testing.md#native-tray-and-overlay) for publishing a separate check-enabled executable.

## Asset extraction

The standalone [extractor entry point](../src/PopRunner.ExtractAssets/Program.cs) accepts a game directory and an output Assets directory. It supports licensed DOS Prince of Persia versions 1.0, 1.1, 1.3, and 1.4 and reads `KID.DAT`, `SHADOW.DAT`, `PV.DAT`, and `PRINCE.DAT` directly. It does not require or execute `PRINCE.EXE` and has no dependency on `System.Drawing`, external converters, or third-party packages.

### Validation and preparation

[AssetExtractor.Prepare](../src/PopRunner.ExtractAssets/AssetExtractor.cs) opens the required data archives, prepares the palettes, decodes every required image, and encodes every resulting PNG in memory. [DatArchive](../src/PopRunner.ExtractAssets/DatArchive.cs) checks SHA-256 against the expected hashes, accepting either the early 1.0/1.1 or later 1.3/1.4 `PRINCE.DAT` data. An unknown hash emits a warning on stderr with the actual and expected hashes and continues parsing. A matching hash is not a substitute for resource validation.

Archive resource directories, bounds, duplicate IDs, and resource checksums are validated. [DatImageDecoder](../src/PopRunner.ExtractAssets/DatImageDecoder.cs) validates palettes, image headers, dimensions, and compressed data before producing straight RGBA pixels. Format details and primary references belong in [research/provenance.md](../research/provenance.md).

**Every required image is parsed and every PNG is prepared before any output directory is created or any destination file is opened.** A parsing failure leaves an existing output directory untouched and creates no new destination. After preparation succeeds, `AssetExtractor.Save` creates the necessary directories and writes the files. A filesystem failure during saving can leave partial output; saving is not a transactional replacement of the Assets tree.

### Output contract

The tool writes 318 PNGs: 251 Prince, 59 Princess, and eight large red potion images. Existing named outputs are overwritten; unrelated files are preserved.

| Folder | Filenames |
| --- | --- |
| `Prince` | `res401.png` through `res619.png`; `res751.png` through `res782.png` |
| `Princess` | `res801.png` through `res817.png`; `res901.png`; `res903.png` through `res930.png`; `res951.png` through `res962.png`; `res981.png` |
| `Potion` | `res163.png`; `res166-red.png` through `res172-red.png` |

[PngEncoder](../src/PopRunner.ExtractAssets/PngEncoder.cs) writes noninterlaced 8-bit RGBA PNGs, preserving decoded dimensions, palette colors, transparency, and blank sprites. Rows are top-down and use the PNG None filter before zlib compression. Potion output retains the original vessel palette and uses red bubbles. These files become embedded resources on the next Core build.

| Exit code | Meaning |
| --- | --- |
| 0 | Successful extraction, including a successful parse after hash warnings; also `--help` or `-h` |
| 1 | Extraction, validation, or save failure |
| 2 | Incorrect argument count |

The [extractor checks](../tests/PopRunner.ExtractAssets.Checks) compare all expected names, dimensions, and exact RGBA pixels against caller-supplied baseline assets, including RGB beneath transparent pixels. They also require byte-identical PNG outputs across supported game versions and verify hash warnings and failure-before-save behavior. Game copies and baseline images are read-only inputs. See the [extractor integration instructions](testing.md#asset-extractor-integration) for the required paths.
