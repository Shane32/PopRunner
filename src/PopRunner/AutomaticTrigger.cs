using System.Security;
using Microsoft.Win32;

namespace PopRunner;

/// <summary>The average automatic playback frequency during eligible computer use.</summary>
/// <remarks>
/// Values are persisted as a registry DWORD and must keep their meanings between releases.
/// Daily and weekly intervals use the usage-hour assumptions in <see cref="AutomaticTrigger"/>.
/// </remarks>
internal enum AutomaticTriggerFrequency
{
    /// <summary>Disables automatic playback while allowing manual playback.</summary>
    Disabled = 0,

    /// <summary>Targets one playback per hour of eligible use, on average.</summary>
    Hourly = 1,

    /// <summary>Targets one playback per assumed day of eligible use, on average.</summary>
    Daily = 2,

    /// <summary>Targets one playback per assumed week of eligible use, on average.</summary>
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
    private const int AssumedHoursPerDay = 8;
    private const int AssumedHoursPerWeek = 40;

    private readonly Func<bool> isEligible;
    private readonly Func<bool> isPlaying;
    private readonly Action playRandom;
    private readonly Func<int, int> nextRandom;
    private readonly string registrySubKey;

    public AutomaticTriggerFrequency Frequency { get; private set; }

    /// <summary>Creates a trigger using the persisted frequency or the Weekly default.</summary>
    /// <param name="isEligible">Reports whether current activity permits automatic playback.</param>
    /// <param name="isPlaying">Reports whether an animation is already playing.</param>
    /// <param name="playRandom">Starts a sequence from the configured random selection pool.</param>
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
            AutomaticTriggerFrequency.Daily => AssumedHoursPerDay * 60,
            AutomaticTriggerFrequency.Weekly => AssumedHoursPerWeek * 60,
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
        return AutomaticTriggerFrequency.Weekly;
    }

    private static bool IsValidFrequency(AutomaticTriggerFrequency frequency) => frequency is
        AutomaticTriggerFrequency.Disabled or AutomaticTriggerFrequency.Hourly or
        AutomaticTriggerFrequency.Daily or AutomaticTriggerFrequency.Weekly;
}
