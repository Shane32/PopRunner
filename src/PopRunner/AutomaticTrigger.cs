using System.Security;
using Microsoft.Win32;

namespace PopRunner;

// Values are persisted as a DWORD. Keep their meanings stable between releases.
internal enum AutomaticTriggerFrequency
{
    Disabled = 0,
    Hourly = 1,
    Daily = 2,
    Weekly = 3
}

/// <summary>
/// Evaluates one automatic-play opportunity when the tray's minute timer fires.
/// Eligibility and playback callbacks keep the policy independent of Win32 state.
/// </summary>
internal sealed class AutomaticTrigger
{
    internal const string RegistrySubKey = @"Software\PopRunner";
    internal const string RegistryValueName = "AutomaticTrigger";

    private readonly Func<bool> isEligible;
    private readonly Func<bool> isPlaying;
    private readonly Action playRandom;
    private readonly Func<int, int> nextRandom;
    private readonly string registrySubKey;

    public AutomaticTriggerFrequency Frequency { get; private set; }

    /// <param name="nextRandom">
    /// Returns a uniform integer from zero, inclusive, to the supplied bound, exclusive.
    /// Checks can supply a deterministic draw without changing the user's settings.
    /// </param>
    /// <param name="registrySubKey">
    /// HKCU subkey used for preferences. Checks supply a temporary subkey rather than
    /// reading or writing the user's PopRunner preference.
    /// </param>
    public AutomaticTrigger(Func<bool> isEligible, Func<bool> isPlaying, Action playRandom,
        Func<int, int>? nextRandom = null, string registrySubKey = RegistrySubKey)
    {
        ArgumentNullException.ThrowIfNull(isEligible);
        ArgumentNullException.ThrowIfNull(isPlaying);
        ArgumentNullException.ThrowIfNull(playRandom);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrySubKey);
        this.isEligible = isEligible;
        this.isPlaying = isPlaying;
        this.playRandom = playRandom;
        this.nextRandom = nextRandom ?? Random.Shared.Next;
        this.registrySubKey = registrySubKey;
        Frequency = ReadFrequency(registrySubKey);
    }

    /// <summary>Persists the selection before making it the active policy.</summary>
    public void SetFrequency(AutomaticTriggerFrequency frequency)
    {
        if (!IsValidFrequency(frequency)) throw new ArgumentOutOfRangeException(nameof(frequency));
        using var key = Registry.CurrentUser.CreateSubKey(registrySubKey, writable: true)
            ?? throw new IOException("Could not open the automatic-trigger preference registry key.");
        key.SetValue(RegistryValueName, (int)frequency, RegistryValueKind.DWord);
        Frequency = frequency;
    }

    /// <summary>
    /// Makes a single draw only while playback is idle and the user is eligible.
    /// It does not replay missed minutes after sleep, lock, or inactivity.
    /// </summary>
    public void OnMinute()
    {
        var opportunities = Frequency switch
        {
            AutomaticTriggerFrequency.Hourly => 60,
            AutomaticTriggerFrequency.Daily => 8 * 60,
            AutomaticTriggerFrequency.Weekly => 40 * 60,
            _ => 0
        };
        if (opportunities == 0 || isPlaying() || !isEligible()) return;
        if (nextRandom(opportunities) == 0) playRandom();
    }

    private static AutomaticTriggerFrequency ReadFrequency(string registrySubKey)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registrySubKey, writable: false);
            if (key?.GetValue(RegistryValueName) is int raw)
            {
                var frequency = (AutomaticTriggerFrequency)raw;
                if (IsValidFrequency(frequency)) return frequency;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            // An inaccessible preference must not prevent the tray from starting.
        }
        return AutomaticTriggerFrequency.Daily;
    }

    private static bool IsValidFrequency(AutomaticTriggerFrequency frequency) => frequency is
        AutomaticTriggerFrequency.Disabled or AutomaticTriggerFrequency.Hourly or
        AutomaticTriggerFrequency.Daily or AutomaticTriggerFrequency.Weekly;
}
