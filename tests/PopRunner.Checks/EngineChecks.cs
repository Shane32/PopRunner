using PopRunner.Core;

namespace PopRunner.Checks;

internal static class EngineChecks
{
    internal static void Run()
    {
        Check.Run("Frame displacement happens on entry, not on each held tick", () =>
        {
            var engine = Engine(TestConfig.Create(
                new FrameStep { Frame = 1, Ticks = 3, Dx = 2 },
                new FrameStep { Frame = 2, Ticks = 2, Dx = 4 }));
            Check.Near(engine.X, 12, "Entering the first frame applies its displacement.");
            engine.Tick();
            engine.Tick();
            Check.That(engine.Frame == 1, "The first frame remains displayed for three ticks.");
            Check.Near(engine.X, 12, "Holding a frame does not repeat its displacement.");
            engine.Tick();
            Check.That(engine.Frame == 2, "The next frame starts after the hold expires.");
            Check.Near(engine.X, 16, "Entering the next frame applies its displacement once.");
            engine.Tick();
            Check.That(!engine.Completed, "The second frame retains its own hold duration.");
            engine.Tick();
            Check.That(engine.Completed, "The final hold completes the animation.");
        });

        Check.Run("Start delay defers initial commands and preserves the first frame's full hold", () =>
        {
            var config = TestConfig.Create(
                new FrameStep { Frame = 1, Ticks = 2, Dx = 2, Flip = true, HidePotion = true, ConsumePotion = true, HidePrimaryActor = true },
                new FrameStep { Frame = 2, Dx = 3 });
            config.Scenarios[0].HasPotion = true;
            config.Scenarios[0].StartDelayTicks = 3;
            var engine = Engine(config);
            Check.That(engine.StartDelayTicksRemaining == 3 && engine.Frame == 1 && engine.ElapsedTicks == 0,
                "The pending delay exposes its first pose without consuming simulation ticks.");
            for (var tick = 1; tick <= 2; tick++)
            {
                engine.Tick();
                Check.That(engine.StartDelayTicksRemaining == 3 - tick && engine.ElapsedTicks == tick &&
                    engine.Frame == 1 && engine.Direction == 1 && engine.PotionVisible && !engine.PotionConsumed && !engine.HidesPrimaryActor && !engine.Completed,
                    "Before the boundary, the clock advances while the first frame's commands remain deferred.");
                Check.Near(engine.X, 10, "The pending first frame does not move the actor.");
            }
            engine.Tick();
            Check.That(engine.StartDelayTicksRemaining == 0 && engine.ElapsedTicks == 3 && engine.Frame == 1 &&
                engine.Direction == -1 && !engine.PotionVisible && engine.PotionConsumed && engine.HidesPrimaryActor && !engine.Completed,
                "The delay boundary enters the first frame and applies its events.");
            Check.Near(engine.X, 12, "The first displacement precedes its flip when the delay ends.");
            engine.Tick();
            Check.That(engine.Frame == 1, "The first frame retains its full hold after the delay boundary.");
            Check.Near(engine.X, 12, "Holding the newly entered frame does not apply its displacement again.");
            engine.Tick();
            Check.That(engine.Frame == 2 && !engine.Completed, "The next frame starts only after the full first hold.");
            Check.Near(engine.X, 9, "Subsequent movement uses the direction established at entry.");
            engine.Tick();
            Check.That(engine.Completed && engine.ElapsedTicks == 6, "The delay adds three ticks without shortening either frame.");
        });

        Check.Run("Start delay keeps actor handoffs synchronized while potion bubbles animate", () =>
        {
            var config = TestConfig.Create(
                new FrameStep { Frame = 1, Ticks = 2, Dx = 2 },
                new FrameStep { Frame = 2, Dx = 1 });
            config.Scenarios[0].StartDelayTicks = 3;
            config.Scenarios[0].TicksPerSecond = 5;
            config.PotionGraphics = new PotionGraphics
            {
                BodyFrame = 8, BubbleFrames = [5, 6, 7, 8], InitialBubbleFrame = 1, TicksPerFrame = 1
            };
            config.Clips["wait"] = new AnimationClip { Frames = [new FrameStep { Frame = 3, Dx = 2 }] };
            config.Clips["handoff"] = new AnimationClip
            {
                Frames = [new FrameStep { Frame = 4, Ticks = 2, Dx = 1, HidePrimaryActor = true }]
            };
            config.Scenarios[0].Actors.Add(new SupportingActor
            {
                Name = "companion", StartX = 30, Direction = -1,
                Steps = [new ScenarioStep { Clip = "wait" }, new ScenarioStep { Clip = "handoff" }]
            });
            var engine = Engine(config);
            var actor = engine.SupportingActors.Single();
            Check.That(actor.Frame == 3 && actor.StartDelayTicksRemaining == 3 && engine.PotionBubbleFrame == 6,
                "The supporting actor shares the pending delay and bubbles start at their configured phase.");
            engine.Tick();
            Check.That(engine.PotionBubbleFrame == 7 && actor.ElapsedTicks == 1 && actor.StartDelayTicksRemaining == 2,
                "Bubbles and the supporting clock advance during the delay.");
            engine.Tick();
            Check.That(engine.PotionBubbleFrame == 8 && actor.Frame == 3 && actor.StepIndex == 0 && engine.PrimaryVisible,
                "The supporting pose and handoff wait while bubbles continue animating.");
            Check.Near(actor.X, 30, "The supporting actor's first movement remains deferred.");
            engine.Tick();
            Check.That(engine.PotionBubbleFrame == 5 && actor.Frame == 3 && actor.StartDelayTicksRemaining == 0 && engine.PrimaryVisible,
                "Starting the actors does not reset the total-elapsed bubble phase or consume the waiting pose.");
            Check.Near(actor.X, 28, "The supporting actor's first movement starts on the shared boundary.");
            Check.Near(engine.X, 12, "The primary actor starts on that same boundary.");
            engine.Tick();
            Check.That(actor.Frame == 4 && !engine.PrimaryVisible && engine.Frame == 1 && engine.PotionBubbleFrame == 6,
                "The handoff follows its original waiting duration after the shared delay.");
            Check.Near(actor.X, 27, "The handoff frame applies its own displacement once.");
            engine.Tick();
            engine.Tick();
            Check.That(engine.Completed && actor.Completed && actor.ElapsedTicks == 6,
                "Both original tracks complete after the same shared shift.");
            Check.Near(actor.TicksPerSecond, 5, "The supporting actor still inherits the scenario clock rate.");
            Check.Near(engine.ElapsedSeconds, 1.2, "Scenario elapsed seconds include the initial delay.");
        });

        Check.Run("One-tick delays retain the first hold, replay resets them, and zero starts immediately", () =>
        {
            var config = TestConfig.Create(new FrameStep { Frame = 1, Dx = 2 });
            config.Scenarios[0].StartDelayTicks = 1;
            var engine = Engine(config);
            Check.Near(engine.X, 10, "Even a one-tick delay defers the initial movement.");
            engine.Tick();
            Check.That(engine.StartDelayTicksRemaining == 0 && !engine.Completed,
                "The boundary starts a one-tick frame rather than completing it.");
            Check.Near(engine.X, 12, "The boundary applies the initial movement.");
            engine.Tick();
            Check.That(engine.Completed && engine.ElapsedTicks == 2, "The one-tick frame completes one tick after entry.");
            var replay = Engine(config);
            Check.That(replay.StartDelayTicksRemaining == 1 && replay.ElapsedTicks == 0 && !replay.Completed,
                "A fresh playback restores the whole initial delay.");
            Check.Near(replay.X, 10, "Replay restores the position before initial movement.");
            config.Scenarios[0].StartDelayTicks = 0;
            var immediate = Engine(config);
            Check.That(immediate.StartDelayTicksRemaining == 0 && immediate.ElapsedTicks == 0,
                "A zero delay has no pending ticks.");
            Check.Near(immediate.X, 12, "Zero delay preserves immediate frame-entry behavior.");
            immediate.Tick();
            Check.That(immediate.Completed && immediate.ElapsedTicks == 1, "Zero delay preserves the original frame duration.");
        });

        Check.Run("Frame movement uses the direction before its flip", () =>
        {
            var engine = Engine(TestConfig.Create(
                new FrameStep { Frame = 1, Dx = 2, Flip = true },
                new FrameStep { Frame = 2, Dx = 3 }));
            Check.Near(engine.X, 12, "The flipping frame moves in the incoming direction.");
            Check.That(engine.Direction == -1, "The frame changes the facing direction.");
            engine.Tick();
            Check.Near(engine.X, 9, "The following frame moves in the new direction.");
        });

        Check.Run("Clip exit movement precedes its flip and the next frame", () =>
        {
            var config = TestConfig.Create(new FrameStep { Frame = 1, Dx = 1 });
            config.Clips["test"].ExitDx = 4;
            config.Clips["test"].ExitFlip = true;
            config.Clips["next"] = new AnimationClip
            {
                Frames = [new FrameStep { Frame = 2, Dx = 2 }]
            };
            config.Scenarios[0].Steps.Add(new ScenarioStep { Clip = "next" });
            var engine = Engine(config);
            engine.Tick();
            Check.That(engine.Frame == 2 && engine.Direction == -1 && engine.StepIndex == 1,
                "The next clip starts after the exit flip.");
            Check.Near(engine.X, 13, "Exit movement uses the old direction; entry movement uses the new one.");
        });

        Check.Run("A starting frame index affects only the first repetition", () =>
        {
            var config = TestConfig.Create(
                new FrameStep { Frame = 1, Dx = 1 },
                new FrameStep { Frame = 2, Dx = 2 },
                new FrameStep { Frame = 3, Dx = 3 });
            config.Scenarios[0].Steps[0].StartFrameIndex = 1;
            config.Scenarios[0].Steps[0].Repeat = 2;
            var engine = Engine(config);
            Check.That(engine.Frame == 2, "The first repetition starts at the requested index.");
            engine.Tick();
            engine.Tick();
            Check.That(engine.Frame == 1, "The second repetition starts at the beginning.");
            Check.Near(engine.X, 16, "The skipped first frame is included in the second repetition.");
            engine.Tick();
            engine.Tick();
            Check.That(!engine.Completed, "The second repetition still holds its last frame.");
            engine.Tick();
            Check.That(engine.Completed && engine.ElapsedTicks == 5, "Both repetitions finish after five displayed ticks.");
            Check.Near(engine.X, 21, "Only the selected first repetition is shortened.");
        });

        Check.Run("Movement targets respect direction and permitted exit frames", () =>
        {
            foreach (var direction in new[] { 1, -1 })
            {
                var config = TestConfig.Create(
                    new FrameStep { Frame = 1, Dx = 2 },
                    new FrameStep { Frame = 2, Dx = 3 });
                config.Clips["test"].Loop = true;
                config.Clips["next"] = new AnimationClip { Frames = [new FrameStep { Frame = 3 }] };
                var scenario = config.Scenarios[0];
                scenario.Direction = direction;
                scenario.Steps[0].UntilX = 10 + direction * 3;
                scenario.Steps[0].ExitOnFrames = [1];
                scenario.Steps.Add(new ScenarioStep { Clip = "next" });
                var engine = Engine(config);
                engine.Tick();
                Check.That(engine.Frame == 2 && engine.StepIndex == 0, "Reaching the target does not skip the current frame's hold.");
                engine.Tick();
                Check.That(engine.Frame == 1 && engine.StepIndex == 0, "The target cannot exit on a disallowed frame.");
                Check.Near(engine.X, 10 + direction * 7, "The loop continues until the permitted exit frame.");
                engine.Tick();
                Check.That(engine.StepIndex == 1 && engine.Frame == 3, "The permitted frame exits toward the next step in either direction.");
            }
        });

        Check.Run("Ticks after completion do not change animation state", () =>
        {
            var config = TestConfig.Create(new FrameStep { Frame = 1, Dx = 2 });
            config.Clips["test"].ExitDx = 3;
            var engine = Engine(config);
            engine.Tick();
            Check.That(engine.Completed, "The single frame completes.");
            engine.Tick();
            engine.Tick();
            Check.That(engine.ElapsedTicks == 1 && engine.Frame == 1 && engine.StepIndex == 0,
                "Completed animations retain their clock and frame state.");
            Check.Near(engine.X, 15, "The exit command is not repeated after completion.");
        });

        Check.Run("Supporting actors share the scenario clock and delay completion", () =>
        {
            var config = TestConfig.Create(new FrameStep { Frame = 1, Dx = 2 });
            config.Scenarios[0].TicksPerSecond = 20;
            config.Clips["actor"] = new AnimationClip { Frames = [new FrameStep { Frame = 2, Ticks = 3 }] };
            config.Scenarios[0].Actors.Add(new SupportingActor
            {
                Name = "companion", StartX = 30, Direction = -1,
                Steps = [new ScenarioStep { Clip = "actor" }]
            });
            var engine = Engine(config);
            var actor = engine.SupportingActors.Single();
            Check.Near(engine.TicksPerSecond, 20, "The scenario overrides the default clock rate.");
            Check.Near(actor.TicksPerSecond, 20, "The supporting actor inherits the scenario clock rate.");
            engine.Tick();
            Check.That(!engine.Completed && !actor.Completed, "The finished primary track waits for its actor.");
            engine.Tick();
            engine.Tick();
            Check.That(engine.Completed && actor.Completed && actor.ElapsedTicks == 3,
                "The supporting actor continues advancing after the primary track finishes.");
            Check.Near(engine.X, 12, "The finished primary track does not repeat its movement while waiting.");
            Check.Near(engine.ElapsedSeconds, 0.15, "Elapsed seconds use the scenario clock.");
        });

        Check.Run("An actor can hide the primary sprite when its frame starts", () =>
        {
            var config = TestConfig.Create(new FrameStep { Frame = 1, Ticks = 3 });
            config.Clips["actor"] = new AnimationClip
            {
                Frames = [new FrameStep { Frame = 2 }, new FrameStep { Frame = 3, HidePrimaryActor = true }]
            };
            config.Scenarios[0].Actors.Add(new SupportingActor
            {
                Name = "companion", Steps = [new ScenarioStep { Clip = "actor" }]
            });
            var engine = Engine(config);
            Check.That(engine.PrimaryVisible, "The primary sprite is visible before the actor's event.");
            engine.Tick();
            Check.That(!engine.PrimaryVisible && engine.SupportingActors[0].HidesPrimaryActor,
                "The actor's hide event applies as soon as its frame is entered.");
            engine.Tick();
            Check.That(!engine.PrimaryVisible && engine.SupportingActors[0].Completed,
                "The hide event persists after the actor finishes.");
        });

        Check.Run("Potion hide and consume events are independent and persist", () =>
        {
            var config = TestConfig.Create(
                new FrameStep { Frame = 1 },
                new FrameStep { Frame = 2, HidePotion = true },
                new FrameStep { Frame = 3, ConsumePotion = true });
            config.Scenarios[0].HasPotion = true;
            var engine = Engine(config);
            Check.That(engine.PotionVisible && !engine.PotionConsumed, "The configured potion starts visible and unconsumed.");
            engine.Tick();
            Check.That(!engine.PotionVisible && !engine.PotionConsumed, "Hiding the potion does not consume it.");
            engine.Tick();
            Check.That(!engine.PotionVisible && engine.PotionConsumed, "Consumption occurs on its own frame.");
            engine.Tick();
            Check.That(!engine.PotionVisible && engine.PotionConsumed, "Potion events persist through completion.");
        });

        Check.Run("Potion bubbles use their configured phase and frame duration", () =>
        {
            var config = TestConfig.Create(new FrameStep { Frame = 1, Ticks = 10 });
            Check.That(Engine(config).PotionBubbleFrame is null, "No bubble frame exists without potion graphics.");
            config.PotionGraphics = new PotionGraphics
            {
                BodyFrame = 8, BubbleFrames = [5, 6, 7], InitialBubbleFrame = 1, TicksPerFrame = 2
            };
            var engine = Engine(config);
            Check.That(engine.PotionBubbleFrame == 6, "The initial phase selects the configured bubble frame.");
            engine.Tick();
            Check.That(engine.PotionBubbleFrame == 6, "A bubble frame is held for its configured duration.");
            engine.Tick();
            Check.That(engine.PotionBubbleFrame == 7, "The next bubble frame appears at the duration boundary.");
            engine.Tick();
            engine.Tick();
            Check.That(engine.PotionBubbleFrame == 5, "Bubble animation wraps to its first frame.");
            engine.Tick();
            engine.Tick();
            Check.That(engine.PotionBubbleFrame == 6, "Bubble animation returns to its initial phase after one cycle.");
        });

        Check.Run("Sprite anchors, facing and parity determine placement", () =>
        {
            foreach (var (direction, oddPixel, expectedLeft) in new[]
            {
                (1, false, 31d), (1, true, 30d), (-1, false, 24d), (-1, true, 25d)
            })
            {
                var config = TestConfig.Create();
                config.Scenarios[0].Direction = direction;
                config.Sprites[1].AnchorX = 2;
                config.Sprites[1].AnchorY = -1;
                config.Sprites[1].OddPixel = oddPixel;
                var placement = Engine(config).GetPlacement(6, 8);
                Check.Near(placement.Left, expectedLeft, "Facing and parity select the horizontal anchor.");
                Check.Near(placement.Top, 67, "The vertical anchor aligns the sprite with the floor.");
                Check.That(placement.Width == 6 && placement.Height == 8 && placement.Mirrored == (direction == 1),
                    "Placement preserves dimensions and reports the correct image orientation.");
            }
        });

        Check.Run("Sprite quantization truncates the anchor toward zero", () =>
        {
            var config = TestConfig.Create();
            config.Stage.PreStretchPixelSize = 0.5;
            config.Scenarios[0].StartX = -1.2;
            var engine = Engine(config);
            Check.Near(engine.GetPlacement(4, 8).Left, -7.1, "Subpixel placement includes the pre-stretch parity offset.");
            config.Stage.QuantizeToPixels = true;
            Check.Near(engine.GetPlacement(4, 8).Left, -7, "Quantization truncates the anchor before subtracting sprite width.");
        });

        Check.Run("Potion placement uses independent body and bubble offsets", () =>
        {
            var config = TestConfig.Create();
            var engine = Engine(config);
            Check.Throws<InvalidOperationException>(() => engine.GetPotionPlacement(3, 5),
                "Potion placement requires potion graphics.");
            config.Scenarios[0].PotionX = 4;
            config.PotionGraphics = new PotionGraphics
            {
                BodyFrame = 7, BubbleFrames = [8],
                BodyOffsetX = -0.4, BodyOffsetY = 2.7,
                BubbleOffsetX = 0.8, BubbleOffsetY = -1.2
            };
            var body = engine.GetPotionPlacement(3, 5);
            var bubbles = engine.GetPotionPlacement(2, 2, bubbles: true);
            Check.Near(body.Left, 11.6, "The body uses its horizontal offset.");
            Check.Near(body.Top, 73.7, "The body uses its floor-relative vertical offset.");
            Check.Near(bubbles.Left, 12.8, "Bubbles use their independent horizontal offset.");
            Check.Near(bubbles.Top, 72.8, "Bubbles use their independent vertical offset.");
            Check.That(!body.Mirrored && body.Width == 3 && body.Height == 5, "Potion graphics retain their dimensions and orientation.");
            config.Stage.QuantizeToPixels = true;
            body = engine.GetPotionPlacement(3, 5);
            bubbles = engine.GetPotionPlacement(2, 2, bubbles: true);
            Check.That(body.Left == 11 && body.Top == 73 && bubbles.Left == 12 && bubbles.Top == 72,
                "Potion placement quantizes its left and bottom coordinates before positioning the image.");
        });

        Check.Run("Potion foot alignment translates both layers without changing their offsets", () =>
        {
            var config = TestConfig.Create();
            config.Scenarios[0].PotionX = 4;
            var graphics = config.PotionGraphics = new PotionGraphics
            {
                BodyFrame = 7, BubbleFrames = [8],
                BodyOffsetX = -0.4, BodyOffsetY = 2.7,
                BubbleOffsetX = 0.8, BubbleOffsetY = -1.2
            };
            var engine = Engine(config);
            var originalBody = engine.GetPotionPlacement(3, 5);
            var originalBubbles = engine.GetPotionPlacement(2, 2, bubbles: true);

            graphics.AlignWithFeet = true;
            var body = engine.GetPotionPlacement(3, 5);
            var bubbles = engine.GetPotionPlacement(2, 2, bubbles: true);
            Check.Near(body.Top + body.Height, 76, "The aligned vessel bottom meets the floor boundary.");
            Check.Near((bubbles.Top + bubbles.Height) - (body.Top + body.Height), -3.9,
                "Bubbles retain their position relative to the vessel.");
            Check.Near(body.Left, originalBody.Left, "Alignment does not move the vessel horizontally.");
            Check.Near(bubbles.Left, originalBubbles.Left, "Alignment does not move bubbles horizontally.");
            Check.Near(graphics.BodyOffsetY, 2.7, "The configured body offset remains intact.");
            Check.Near(graphics.BubbleOffsetY, -1.2, "The configured bubble offset remains intact.");

            config.Stage.QuantizeToPixels = true;
            body = engine.GetPotionPlacement(3, 5);
            bubbles = engine.GetPotionPlacement(2, 2, bubbles: true);
            Check.That(body.Top == 71 && bubbles.Top == 70 && body.Left == 11 && bubbles.Left == 12,
                "Fractional offsets are translated before the aligned placements are quantized.");

            graphics.AlignWithFeet = false;
            Check.Near(engine.GetPotionPlacement(3, 5).Top, 73, "Disabling alignment restores the quantized vessel placement.");
            Check.Near(engine.GetPotionPlacement(2, 2, bubbles: true).Top, 72,
                "Disabling alignment restores the quantized bubble placement.");
            config.Stage.QuantizeToPixels = false;
            Check.That(engine.GetPotionPlacement(3, 5) == originalBody &&
                engine.GetPotionPlacement(2, 2, bubbles: true) == originalBubbles,
                "Disabling alignment restores both original fractional placements.");
        });
    }

    private static AnimationEngine Engine(AnimationConfig config) => new(config, config.Scenarios[0]);
}
