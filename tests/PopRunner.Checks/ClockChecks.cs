using PopRunner.Core;

namespace PopRunner.Checks;

internal static class ClockChecks
{
    internal static void Run()
    {
        Check.Run("Clock retains fractional time between updates", () =>
        {
            var clock = new AnimationClock(4, 1);
            var ticks = 0;
            Check.That(clock.Advance(0.1, 1, () => ticks++) == 0, "A partial interval advanced the clock.");
            Check.That(clock.Advance(0.2, 1, () => ticks++) == 1 && ticks == 1, "Elapsed intervals were not accumulated.");
            Check.That(clock.Advance(0.2, 1, () => ticks++) == 1 && ticks == 2, "The fractional remainder was lost.");
        });

        Check.Run("Clock speed scales elapsed time", () =>
        {
            var clock = new AnimationClock(4, 1);
            var ticks = 0;
            Check.That(clock.Advance(0.25, 0.5, () => ticks++) == 0, "Half speed advanced too soon.");
            Check.That(clock.Advance(0.25, 0.5, () => ticks++) == 1, "Half speed did not accumulate two updates.");
            Check.That(clock.Advance(0.25, 2, () => ticks++) == 2 && ticks == 3, "Double speed did not advance two ticks.");
        });

        Check.Run("Clock limits stall catch-up before applying speed", () =>
        {
            var clock = new AnimationClock(8, 0.5);
            var ticks = 0;
            Check.That(clock.Advance(20, 2, () => ticks++) == 8 && ticks == 8, "The catch-up limit or speed was ignored.");
            Check.That(clock.Advance(0, 1, () => ticks++) == 0, "Discarded stall time leaked into the next update.");
        });

        Check.Run("Clock reset clears accumulated time", () =>
        {
            var clock = new AnimationClock(4, 1);
            var ticks = 0;
            clock.Advance(0.2, 1, () => ticks++);
            clock.Reset();
            Check.That(clock.Advance(0.1, 1, () => ticks++) == 0 && ticks == 0, "Reset retained the previous fraction.");
        });

        Check.Run("Clock ignores invalid updates without changing its accumulator", () =>
        {
            var clock = new AnimationClock(4, 1);
            var ticks = 0;
            clock.Advance(0.1, 1, () => ticks++);
            foreach (var (elapsed, speed) in new[]
            {
                (-1d, 1d), (double.NaN, 1d), (double.PositiveInfinity, 1d),
                (1d, 0d), (1d, -1d), (1d, double.NaN), (1d, double.PositiveInfinity)
            })
                Check.That(clock.Advance(elapsed, speed, () => ticks++) == 0, "An invalid update advanced the clock.");
            Check.That(clock.Advance(0.15, 1, () => ticks++) == 1 && ticks == 1, "An invalid update changed accumulated time.");
        });

        Check.Run("Display update frequency does not change engine state", () =>
        {
            static AnimationEngine Advance(IEnumerable<double> intervals)
            {
                var config = TestConfig.Create(new FrameStep { Frame = 1, Dx = 2 }, new FrameStep { Frame = 2, Dx = 1 });
                config.Scenarios[0].Steps[0].Repeat = 100;
                var engine = new AnimationEngine(config, config.Scenarios[0]);
                var clock = new AnimationClock(10, 0.25);
                foreach (var interval in intervals) clock.Advance(interval, 1, engine.Tick);
                return engine;
            }
            var frequent = Advance(Enumerable.Repeat(1.0 / 60, 120));
            var faster = Advance(Enumerable.Repeat(1.0 / 144, 288));
            var uneven = Advance(Enumerable.Range(0, 40).SelectMany(_ => new[] { 0.01, 0.04 }));
            Check.That(frequent.ElapsedTicks == 20 && faster.ElapsedTicks == 20 && uneven.ElapsedTicks == 20,
                "Equal elapsed durations produced different tick counts.");
            Check.That(frequent.Frame == faster.Frame && frequent.Frame == uneven.Frame, "Display frequency changed the frame.");
            Check.Near(frequent.X, faster.X, "Display frequency changed position.");
            Check.Near(frequent.X, uneven.X, "Uneven updates changed position.");
        });
    }
}
