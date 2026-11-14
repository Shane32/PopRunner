# Testing

Run these PowerShell commands from the repository root. Use the SDK selected by [global.json](../global.json). Before building Core or either desktop app, generate `src/PopRunner.Core/Assets` from a licensed game copy as described in the [README](../README.md). The extractor builds independently of those assets.

All test output belongs under the ignored `artifacts` directory. These checks do not require third-party test packages.

## Core behavior

```powershell
dotnet build PopRunner.slnx -c Release
dotnet run --project tests/PopRunner.Checks -c Release
```

[PopRunner.Checks](../tests/PopRunner.Checks) uses small synthetic configurations to check frame holds, movement and flip ordering, repetition, travel targets, supporting actors, potion events, placement, clocks, JSON shorthand/object equivalence, serialization, and validation. Resource checks inspect the embedded PNGs, exercise filename/path lookup, and load the packaged configuration.

The suite reports named test cases. It does not maintain a second copy of the production choreography or stored traces from `animation.json`; configuration changes do not require updating duplicate pose and timing data in these tests.

To compile and run the same suite with Native AOT on Windows x64:

```powershell
dotnet publish tests/PopRunner.Checks -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot/checks
artifacts\aot\checks\PopRunner.Checks.exe
```

This requires the Windows Native AOT C++ compiler toolchain. The check project roots all of Core so unused methods are analyzed too. Core uses source-generated JSON metadata and enables AOT compatibility analyzers. The Windows Forms sample retains its normal managed build.

## Native tray and overlay

Normal tray builds omit the self-test code. Set `EnableNativeChecks=true` to define `POP_RUNNER_CHECKS` and include its `#if` blocks, helper Win32 imports, and overlay diagnostics. This property is independent of Debug/Release; keep the check-enabled executable in a separate output directory:

```powershell
dotnet publish src/PopRunner -c Release -p:EnableNativeChecks=true -o artifacts/native-with-checks
$check = Start-Process 'artifacts/native-with-checks/PopRunner.exe' `
  -ArgumentList '--self-test', 'artifacts/native-checks' -Wait -PassThru
Get-Content artifacts/native-checks/native-smoke-checks.txt
if ($check.ExitCode -ne 0) { throw 'Native checks failed; see the report.' }
```

Run this on an interactive Windows desktop with Explorer running. An isolated desktop without Explorer cannot exercise the notification-area shell behavior.

[NativeSmokeChecks.cs](../src/PopRunner/NativeSmokeChecks.cs), [NativeScenarioChecks.cs](../src/PopRunner/NativeScenarioChecks.cs), and their helpers check:

- All locally generated sprites, PNG pixels, premultiplied alpha, nearest-neighbor scaling, clipping, and compositing.
- Random selection, the bold default menu item, numbered scenario submenu, double-click and keyboard activation, ignored single clicks, Win32 timer dispatch, and Exit.
- Automatic-trigger menu selection, registry persistence, activity gates, microphone-record traversal, and chance calculations using injected activity and random draws.
- Primary-monitor/taskbar bounds, topmost and nonactivating styles, configured interaction placement, complete routes, replay, and unchanged source configuration.
- Entrance delays with animated potion bubbles and waiting actors, potion lifecycle and alignment modes, greeting handoffs, and careful-step entry/hold/retreat.
- Mouse input reaching a separate test process through both opaque and transparent overlay pixels.

The input check briefly displays its own target window, moves the mouse, and clicks only that window. It restores the mouse position and attempts to restore the previous foreground window afterward. The popup-menu check dismisses its own menu. **The checks do not open the About dialog.**

Preference and synthetic microphone tests use unique temporary keys under `HKEY_CURRENT_USER\Software\PopRunner.Tests`, then remove them. They do not change the user's `Software\PopRunner` preference. A separate probe reads the real desktop activity state without changing it.

The output directory contains `native-smoke-checks.txt` with the result or failure details, `native-check-progress.txt`, `native-scenarios.txt`, transparent PNG previews, and click-target diagnostic files. The scenario checks derive pose and timing expectations from the loaded configuration; independent engine behavior is covered by the Core fixtures.

Omit `EnableNativeChecks` or set it to `false` to publish the normal app. That build does not accept `--self-test`:

```powershell
dotnet publish src/PopRunner -c Release -o artifacts/native
```

## Windows Forms capture checks

Publish the sample, then run its real controls and paint paths in capture mode on an interactive Windows desktop:

```powershell
dotnet publish src/PopRunner.Sample -c Release --self-contained false -o artifacts/demo
$check = Start-Process 'artifacts/demo/PopRunner.Sample.exe' `
  -ArgumentList '--capture-preview', 'artifacts/previews/preview.png' -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'Sample checks failed; see artifacts/previews/preview.png.error.txt.' }
Get-Content artifacts/previews/ui-smoke-checks.txt
```

The sample uses the embedded configuration and exits after capture. [MainForm.CapturePreview](../src/PopRunner.Sample/MainForm.cs) checks playback, pause, one-tick stepping, speed, guides, selection, keyboard shortcuts, restart/replay, clock changes, cycling, embedded-default loading, and the read-only JSON viewer. It also verifies that invalid settings or a missing image preserve the current playback.

Captures cover every configured scene, entrance lead-ins, potion bubble advancement/reset and drinking, both greetings' approach/contact/embrace, potion braking/turning/departure boundaries, both careful steps' entry/hold/retreat, and the minimum window with alignment guides. The workflow checks cycling through sequences 5–10 and back to 1.

The requested `preview.png` and additional `preview-*.png` files are written beside `ui-smoke-checks.txt`. A failure returns exit code 1 and writes `<requested-path>.error.txt`. Inspect the PNGs to assess alignment and appearance; successful assertions alone do not replace visual review.

## Asset-extractor integration

The [extractor integration checks](../tests/PopRunner.ExtractAssets.Checks/Program.cs) accept an extractor DLL path, an existing local asset baseline, an output directory, and one or more licensed game directories. Build the extractor explicitly before supplying its DLL. To compare all four supported versions, replace these example game paths:

```powershell
dotnet build src/PopRunner.ExtractAssets -c Release
dotnet run --project tests/PopRunner.ExtractAssets.Checks -c Release -- `
  'src/PopRunner.ExtractAssets/bin/Release/net11.0/PopRunner.ExtractAssets.dll' `
  'src/PopRunner.Core/Assets' 'artifacts/extractor-checks' `
  'C:\Games\Prince-1.0' `
  'C:\Games\Prince-1.1' `
  'C:\Games\Prince-1.3' `
  'C:\Games\Prince-1.4'
```

Each game directory must contain `KID.DAT`, `SHADOW.DAT`, `PV.DAT`, and `PRINCE.DAT`. Game directories and baseline assets are read-only inputs. The checks create a unique `run-<id>` folder beneath the output directory and report that location on completion.

For each input, the checks compare all 318 output filenames, dimensions, and exact RGBA pixels against the baseline, including RGB beneath transparent pixels. Generated PNG files must also be byte-identical between game versions. The app's PNG reader independently checks format, CRCs, dimensions, and rendered pixels.

The checks modify copies of the data to verify an unknown-hash warning still produces the same assets. They also corrupt a late image and truncate compressed image data, verifying that parsing errors leave an existing destination untouched and do not create an absent destination. A four-version run performs 1,590 image comparisons, including the modified-hash input.

Keep licensed game data, generated copyrighted assets, and test captures out of Git. See the [third-party notices](../THIRD-PARTY-NOTICES.md) for the artwork's separate copyright restrictions.
