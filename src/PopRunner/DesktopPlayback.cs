using PopRunner.Core;

namespace PopRunner;

/// <summary>Shares random selection between the tray's default click and menu command.</summary>
internal sealed class DesktopPlayback
{
    private readonly Action<int> play;
    private readonly int scenarioCount;
    internal IReadOnlyList<int> RandomScenarioIndices { get; }

    internal DesktopPlayback(AnimationConfig config, Action<int> play)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(play);
        this.play = play;
        scenarioCount = config.Scenarios.Count;
        var indices = config.Desktop.RandomScenarioIds.Select(id => config.Scenarios.FindIndex(s => s.Id == id)).ToArray();
        if (indices.Length == 0 || indices.Any(index => index < 0))
            throw new InvalidDataException("The desktop random selection needs valid scenario IDs.");
        RandomScenarioIndices = Array.AsReadOnly(indices);
    }

    internal void PlayRandom() => Play(RandomScenarioIndices[Random.Shared.Next(RandomScenarioIndices.Count)]);

    internal void Play(int scenarioIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scenarioIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(scenarioIndex, scenarioCount);
        play(scenarioIndex);
    }
}
