using System.Runtime.InteropServices;
using PopRunner.Core;

namespace PopRunner;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
#if POP_RUNNER_CHECKS
            if (args.Length > 0) return NativeSmokeChecks.Run(args);
#else
            if (args.Length > 0) throw new ArgumentException("This build does not accept command-line arguments.");
#endif
            using var instance = new Mutex(true, @"Local\PopRunner.NativeTray", out var firstInstance);
            if (!firstInstance) return 0;

            var config = AnimationConfig.Load();
            using var overlay = new DesktopOverlay(config);
            var playback = new DesktopPlayback(config, overlay.Play);
            var automaticTrigger = new AutomaticTrigger(DesktopActivity.IsEligible, () => overlay.IsPlaying, playback.PlayRandom);
            using var tray = new TrayHost(config.Scenarios, playback.PlayRandom, playback.Play, () => { }, automaticTrigger);
            return tray.Run();
        }
        catch (Exception error)
        {
            MessageBoxW(0, error.Message, "PopRunner", 0x10);
            return 1;
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint flags);
}
