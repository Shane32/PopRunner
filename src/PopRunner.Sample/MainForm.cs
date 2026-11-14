using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;
using PopRunner.Core;

namespace PopRunner.Sample;

public partial class MainForm : Form
{
    private AnimationConfig config = null!;
    private SpriteAtlas atlas = null!;
    private AnimationEngine engine = null!;
    private AnimationClock clock = null!;
    private readonly string? capturePath;
    private readonly Stopwatch stopwatch = new();
    private double lastTime;
    private bool playing = true;
    private bool ready;
    public int ExitCode { get; private set; }

    public MainForm()
    {
        InitializeComponent();
    }

    public MainForm(string? capturePath) : this()
    {
        this.capturePath = capturePath;
        ApplyConfiguration();
    }

    protected override void OnLoad(EventArgs e)
    {
        if (!DesignMode && !ready) ApplyConfiguration();
        base.OnLoad(e);
    }

    private void ApplyConfiguration(Stream? json = null)
    {
        // Validate and decode before replacing any active state or disposing its images.
        var nextConfig = json == null ? AnimationConfig.Load() : AnimationConfig.Load(json);
        var nextAtlas = new SpriteAtlas(nextConfig);
        var oldAtlas = atlas;
        var selectedId = engine?.Scenario.Id;
        ready = false;
        config = nextConfig;
        atlas = nextAtlas;
        scenarioComboBox.BeginUpdate();
        scenarioComboBox.Items.Clear();
        for (var index = 0; index < config.Scenarios.Count; index++)
            scenarioComboBox.Items.Add($"{index + 1}. {config.Scenarios[index].Title}");
        scenarioComboBox.SelectedIndex = Math.Max(0, config.Scenarios.FindIndex(s => s.Id == selectedId));
        scenarioComboBox.EndUpdate();
        cycleCheckBox.Checked = config.Stage.AutoAdvance;
        guidesCheckBox.Checked = config.Stage.ShowGuides;
        ready = true;
        StartScenario(scenarioComboBox.SelectedIndex);
        oldAtlas?.Dispose();
        configStatusLabel.ForeColor = SystemColors.ControlText;
        configStatusLabel.Text = $"Embedded animation.json - {config.Sprites.Count} frame records";
        configStatusLabel.ToolTipText = $"{typeof(AnimationConfig).Assembly.GetName().Name} assembly resources";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        stopwatch.Start();
        lastTime = 0;
        stageView.Focus();
        if (capturePath == null) renderTimer.Start();
        else BeginInvoke(() =>
        {
            try { CapturePreview(capturePath); }
            catch (Exception ex)
            {
                ExitCode = 1;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capturePath))!);
                File.WriteAllText(capturePath + ".error.txt", ex.ToString());
            }
            finally { Close(); }
        });
    }

    private void StartScenario(int index)
    {
        if (index < 0 || index >= config.Scenarios.Count) return;
        engine = new AnimationEngine(config, config.Scenarios[index]);
        stageView.SetScene(config, atlas, engine);
        clock = new AnimationClock(engine.TicksPerSecond, config.Clock.MaxCatchUpSeconds);
        timingLabel.Text = $"{engine.TicksPerSecond:0.##} animation ticks / second";
        lastTime = stopwatch.Elapsed.TotalSeconds;
        playing = true;
        if (scenarioComboBox.SelectedIndex != index) scenarioComboBox.SelectedIndex = index;
        UpdateDisplay();
    }

    private void RenderFrame(object? sender, EventArgs e)
    {
        var now = stopwatch.Elapsed.TotalSeconds;
        var elapsed = now - lastTime;
        lastTime = now;
        // The timer only wakes the UI. Stopwatch + AnimationClock determine actual DOS ticks.
        if (!ready || !playing || WindowState == FormWindowState.Minimized) return;
        var count = clock.Advance(elapsed, (double)speedInput.Value, () =>
        {
            if (!engine.Completed) engine.Tick();
        });
        if (engine.Completed)
        {
            if (cycleCheckBox.Checked) StartScenario((scenarioComboBox.SelectedIndex + 1) % config.Scenarios.Count);
            else playing = false;
        }
        if (count > 0) UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        stageView.Invalidate();
        var displayed = engine.PrimaryVisible ? engine : engine.SupportingActors.First(a => a.HidesPrimaryActor);
        descriptionLabel.Text = $"{engine.Scenario.Subtitle}    Route: {engine.Scenario.Route}";
        frameInfoLabel.Text = $"Pose: F{displayed.Frame:000}     Sprite: {Path.GetFileName(displayed.Sprite.Image)}     Facing: {(displayed.Direction > 0 ? "Right" : "Left")}     Position: {displayed.X:0.##} DOS units";
        framesGroupBox.Text = $"Sprite frames - {(displayed == engine ? engine.Phase : displayed.Scenario.Title + ": " + displayed.Phase)}";
        frameStripView.SetFrames(config, atlas, displayed);
        phaseStatusLabel.Text = engine.Phase;
        elapsedStatusLabel.Text = $"{engine.ElapsedSeconds:0.00}s   Tick {engine.ElapsedTicks:000}";
        playButton.Text = engine.Completed ? "Replay" : playing ? "Pause" : "Play";
    }

    private void ScenarioChanged()
    {
        if (ready && scenarioComboBox.SelectedIndex >= 0 && config.Scenarios[scenarioComboBox.SelectedIndex] != engine.Scenario)
            StartScenario(scenarioComboBox.SelectedIndex);
    }

    private void TogglePlay()
    {
        if (engine.Completed) StartScenario(scenarioComboBox.SelectedIndex);
        else playing = !playing;
        clock.Reset();
        lastTime = stopwatch.Elapsed.TotalSeconds;
        UpdateDisplay();
    }
    private void Restart() => StartScenario(scenarioComboBox.SelectedIndex);
    private void StepFrame()
    {
        if (engine.Completed) Restart();
        playing = false;
        clock.Reset();
        engine.Tick();
        UpdateDisplay();
    }
    private void ViewConfig()
    {
        using var viewer = CreateConfigurationViewer();
        viewer.ShowDialog(this);
    }

    private Form CreateConfigurationViewer()
    {
        using var stream = AnimationResources.OpenConfiguration();
        using var reader = new StreamReader(stream);
        var viewer = new Form
        {
            Text = "Embedded animation.json - rebuild to modify",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(900, 700),
            MinimumSize = new Size(600, 400),
            ShowInTaskbar = false,
            MinimizeBox = false,
            Padding = new Padding(10)
        };
        var monoFont = new Font("Consolas", 10);
        var jsonText = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = monoFont,
            BackColor = SystemColors.Window,
            Text = reader.ReadToEnd().ReplaceLineEndings(Environment.NewLine)
        };
        var instructions = new Label
        {
            Text = "This JSON is embedded in PopRunner.Core. Change it in the Core project and rebuild to modify the animation settings.",
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 10)
        };
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(80, 28) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(closeButton);
        viewer.Controls.Add(jsonText);
        viewer.Controls.Add(instructions);
        viewer.Controls.Add(buttons);
        viewer.AcceptButton = closeButton;
        viewer.CancelButton = closeButton;
        viewer.Disposed += (_, _) => monoFont.Dispose();
        return viewer;
    }

    private void ReloadConfig() => TryReloadConfiguration();

    private bool TryReloadConfiguration(Stream? json = null)
    {
        try
        {
            ApplyConfiguration(json);
            return true;
        }
        catch (Exception ex)
        {
            configStatusLabel.ForeColor = Color.Firebrick;
            configStatusLabel.Text = $"Config not loaded: {ex.Message}";
            configStatusLabel.ToolTipText = ex.ToString();
            return false;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        => TryShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && TryShortcut(e.KeyData))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    private bool TryShortcut(Keys key)
    {
        if (!ready || speedInput.ContainsFocus || scenarioComboBox.DroppedDown) return false;
        switch (key)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.R: Restart(); return true;
            case Keys.Right: StepFrame(); return true;
            case Keys.F5: ReloadConfig(); return true;
            case Keys.G: guidesCheckBox.Checked = !guidesCheckBox.Checked; return true;
            case Keys.D0 when config.Scenarios.Count >= 10:
                StartScenario(9);
                return true;
            case >= Keys.D1 and <= Keys.D9:
                var index = (int)key - (int)Keys.D1;
                if (index >= config.Scenarios.Count) return false;
                StartScenario(index);
                return true;
            default: return false;
        }
    }

    // DrawToBitmap uses these Forms controls' real paint paths, including the GDI+ sprite surface.
    private void CapturePreview(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        void AdvanceUntil(Func<bool> condition, string message)
        {
            var guard = 0;
            while (!condition() && !engine.Completed && guard++ < 10000) engine.Tick();
            Require(condition(), message);
        }
        SpritePlacement PrincePlacement()
        {
            var image = atlas.Get(engine.Frame, engine.Direction);
            return engine.GetPlacement(image.Width, image.Height);
        }
        bool PrinceFullyOnStage()
        {
            var placement = PrincePlacement();
            return engine.PrimaryVisible && placement.Left >= 0 && placement.Left + placement.Width <= config.Stage.Width;
        }
        bool PrinceVisibleOnStage()
        {
            var placement = PrincePlacement();
            return engine.PrimaryVisible && placement.Left < config.Stage.Width && placement.Left + placement.Width > 0;
        }
        bool PrinceFullyOffStage()
        {
            var placement = PrincePlacement();
            return placement.Left + placement.Width <= 0 || placement.Left >= config.Stage.Width;
        }
        static Keys ScenarioShortcut(int index) => index == 9 ? Keys.D0 : (Keys)((int)Keys.D1 + index);
        Require(this is Form && stageView is Control, "Preview is not a Windows Forms surface.");
        StartScenario(config.Scenarios.FindIndex(s => s.HasPotion));
        var initialLeadX = engine.X;
        var initialLeadFrame = engine.Frame;
        Require(engine.StartDelayTicksRemaining == engine.Scenario.StartDelayTicks,
            "Scenario selection did not initialize the configured entrance delay.");
        playButton.PerformClick();
        Require(!playing, "Pause did not stop playback.");
        var tick = engine.ElapsedTicks;
        stepButton.PerformClick();
        Require(!playing && engine.ElapsedTicks == tick + 1, "Step did not advance exactly one tick.");
        Require(engine.StartDelayTicksRemaining == Math.Max(0, engine.Scenario.StartDelayTicks - 1) &&
            (engine.StartDelayTicksRemaining == 0 || engine.X == initialLeadX && engine.Frame == initialLeadFrame),
            "Stepping during the entrance delay advanced the Prince's choreography.");
        restartButton.PerformClick();
        Require(playing && engine.ElapsedTicks == 0 && engine.PotionVisible &&
            engine.StartDelayTicksRemaining == engine.Scenario.StartDelayTicks,
            "Restart did not restore the potion and entrance delay.");
        var initialBubbles = engine.PotionBubbleFrame;
        playButton.PerformClick();
        Require(!playing && engine.PotionBubbleFrame == initialBubbles, "Pause changed the potion bubble phase.");
        stepButton.PerformClick();
        var potionGraphics = config.PotionGraphics!;
        var expectedBubble = potionGraphics.BubbleFrames[(potionGraphics.InitialBubbleFrame + 1 / potionGraphics.TicksPerFrame) %
            potionGraphics.BubbleFrames.Length];
        Require(engine.ElapsedTicks == 1 && engine.PotionBubbleFrame == expectedBubble,
            "Stepping did not keep potion bubbles on their configured clock during the entrance delay.");
        restartButton.PerformClick();
        Require(engine.PotionBubbleFrame == initialBubbles, "Restart did not restore the initial bubble phase.");
        scenarioComboBox.SelectedIndex = 0;
        Require(engine.Scenario == config.Scenarios[0], "Scenario selector did not update playback.");
        Require(!string.IsNullOrWhiteSpace(scenarioComboBox.Text), "Scenario selector has no visible caption.");
        speedInput.Value = 0.5m;
        Require(speedInput.Value == 0.5m, "Speed control did not update.");
        speedInput.Value = 1;
        guidesCheckBox.Checked = true;
        Require(stageView.ShowGuides, "Guide checkbox did not update the surface.");
        guidesCheckBox.Checked = false;
        reloadButton.PerformClick();
        Require(engine.ElapsedTicks == 0 && engine.Scenario.Id == config.Scenarios[0].Id, "Reload lost selection or failed to reset.");
        var activeEngine = engine;
        using (var invalidJson = new MemoryStream(Encoding.UTF8.GetBytes("{\"clock\":{\"ticksPerSecond\":0}}")))
        {
            Require(!TryReloadConfiguration(invalidJson) && ReferenceEquals(engine, activeEngine),
                "Invalid JSON settings replaced the running animation.");
        }
        var brokenAssets = JsonSerializer.Deserialize<AnimationConfig>(JsonSerializer.Serialize(config))!;
        brokenAssets.Sprites.First().Value.Image = "Assets/Prince/missing-ui-check.png";
        using (var missingResourceJson = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(brokenAssets)))
        {
            Require(!TryReloadConfiguration(missingResourceJson) && ReferenceEquals(engine, activeEngine),
                "A missing embedded PNG resource replaced the running animation.");
        }
        Require(TryReloadConfiguration(), "The embedded defaults could not be restored.");
        using (var viewer = CreateConfigurationViewer())
        {
            var jsonText = viewer.Controls.OfType<TextBox>().Single();
            Require(jsonText.ReadOnly && jsonText.Multiline && jsonText.Text.Contains("potion-direct-return-left"),
                "The configuration viewer did not show the read-only embedded JSON.");
            viewer.Show(this);
            viewer.Update();
            using var viewerBitmap = new Bitmap(viewer.Width, viewer.Height);
            viewer.DrawToBitmap(viewerBitmap, new Rectangle(Point.Empty, viewer.Size));
            viewerBitmap.Save(Path.Combine(directory, "preview-embedded-json.png"), ImageFormat.Png);
            viewer.Close();
        }
        var greetingIndex = config.Scenarios.FindIndex(s => s.Id == "greet-princess");
        Require(greetingIndex >= 0, "The Princess greeting is missing from the scenario selector.");
        stageView.Focus();
        Require(TryShortcut(ScenarioShortcut(greetingIndex)) && scenarioComboBox.SelectedIndex == greetingIndex,
            "The greeting's number shortcut did not select the greeting.");
        Require(engine.TicksPerSecond == 10 && timingLabel.Text.StartsWith("10 "), "Greeting did not select the cutscene clock.");
        while (engine.PrimaryVisible && !engine.Completed) engine.Tick();
        restartButton.PerformClick();
        Require(engine.PrimaryVisible && engine.SupportingActors.Single().Frame == 1020 && engine.ElapsedTicks == 0 &&
            engine.StartDelayTicksRemaining == engine.Scenario.StartDelayTicks &&
            engine.SupportingActors.Single().StartDelayTicksRemaining == engine.StartDelayTicksRemaining,
            "Restart did not reset both greeting actors.");
        var mirroredGreetingIndex = config.Scenarios.FindIndex(s => s.Id == "greet-princess-left");
        Require(mirroredGreetingIndex >= 0, "The mirrored Princess greeting is missing from the scenario selector.");
        Require(TryShortcut(ScenarioShortcut(mirroredGreetingIndex)) && scenarioComboBox.SelectedIndex == mirroredGreetingIndex,
            "The mirrored greeting's number shortcut did not select the greeting.");
        var mirroredGreeting = config.Scenarios[mirroredGreetingIndex];
        Require(engine.Scenario == mirroredGreeting && engine.Direction == mirroredGreeting.Direction,
            "The scenario selector did not start the mirrored greeting.");
        AdvanceUntil(() => !engine.PrimaryVisible, "The mirrored greeting did not reach its reunion handoff.");
        restartButton.PerformClick();
        var princess = mirroredGreeting.Actors.Single();
        var initialPrincessStep = princess.Steps[0];
        var initialPrincessFrame = config.Clips[initialPrincessStep.Clip].Frames[initialPrincessStep.StartFrameIndex].Frame;
        Require(engine.ElapsedTicks == 0 && engine.PrimaryVisible && engine.SupportingActors.Single().Frame == initialPrincessFrame &&
            engine.SupportingActors.Single().Direction == princess.Direction &&
            engine.StartDelayTicksRemaining == mirroredGreeting.StartDelayTicks &&
            engine.SupportingActors.Single().StartDelayTicksRemaining == engine.StartDelayTicksRemaining,
            "Restart did not restore both mirrored greeting actors.");
        AdvanceUntil(() => engine.Completed, "The mirrored greeting did not complete.");
        UpdateDisplay();
        Require(playButton.Text == "Replay", "The completed mirrored greeting did not offer replay.");
        playButton.PerformClick();
        Require(playing && engine.ElapsedTicks == 0 && engine.Scenario == mirroredGreeting && engine.PrimaryVisible,
            "Replay did not restart the selected mirrored greeting.");
        foreach (var potionScenario in config.Scenarios.Where(s => s.HasPotion))
        {
            var id = potionScenario.Id;
            var direction = potionScenario.Direction;
            var directIndex = config.Scenarios.IndexOf(potionScenario);
            var shortcut = ScenarioShortcut(directIndex);
            Require(TryShortcut(shortcut) && scenarioComboBox.SelectedIndex == directIndex &&
                engine.Direction == direction && engine.PotionVisible, $"Shortcut {shortcut} did not select {id}.");
            AdvanceUntil(() => engine.ClipName != "run", $"{id} did not finish its incoming run.");
            Require(engine.ClipName == "shortStop", $"{id} did not brake before drinking.");
            var brakingFrames = new List<int>();
            while (engine.ClipName == "shortStop" && !engine.Completed)
            {
                Require(engine.Direction == direction && engine.PotionVisible && !engine.PotionConsumed,
                    $"{id} changed facing or picked up its potion while braking.");
                brakingFrames.Add(engine.Frame);
                engine.Tick();
            }
            Require(brakingFrames.SequenceEqual([53, 54, 55, 56, 49, 50, 51, 52]),
                $"{id} did not use exactly the eight braking poses.");
            Require(engine.ClipName == "shortStand" && engine.Frame == 15 && engine.Direction == direction && engine.PotionVisible && !engine.PotionConsumed,
                $"{id} did not stand for one tick before drinking.");
            engine.Tick();
            Require(engine.ClipName == "drink" && engine.Direction == direction && !engine.PotionVisible && !engine.PotionConsumed,
                $"{id} did not drink immediately after its one-tick standing pose.");
            AdvanceUntil(() => engine.PotionConsumed, $"{id} did not consume its potion.");
            AdvanceUntil(() => engine.ClipName == "shortStandingTurn", $"{id} did not turn after drinking.");
            var turningFrames = new List<int>();
            while (engine.ClipName == "shortStandingTurn" && !engine.Completed)
            {
                turningFrames.Add(engine.Frame);
                engine.Tick();
            }
            Require(turningFrames.SequenceEqual([45, 46, 47, 48, 49, 50, 51]) && engine.ClipName == "start" && engine.Direction == -direction,
                $"{id} did not skip pose 52 and start running immediately after its shortened turn.");
            restartButton.PerformClick();
            Require(engine.ElapsedTicks == 0 && engine.ClipName == "run" && engine.Direction == direction &&
                engine.PotionVisible && !engine.PotionConsumed, $"Restart did not restore {id}'s running entry and potion.");
        }
        cycleCheckBox.Checked = true;
        StartScenario(config.Scenarios.FindIndex(s => s.Actors.Count > 0));
        var cycleRoute = new List<int> { scenarioComboBox.SelectedIndex + 1 };
        for (var expectedIndex = scenarioComboBox.SelectedIndex + 1; expectedIndex < config.Scenarios.Count; expectedIndex++)
        {
            while (!engine.Completed) engine.Tick();
            RenderFrame(this, EventArgs.Empty);
            Require(scenarioComboBox.SelectedIndex == expectedIndex, $"Cycling skipped scenario {expectedIndex + 1}.");
            cycleRoute.Add(scenarioComboBox.SelectedIndex + 1);
        }
        while (!engine.Completed) engine.Tick();
        RenderFrame(this, EventArgs.Empty);
        Require(scenarioComboBox.SelectedIndex == 0 && engine.TicksPerSecond == config.Clock.TicksPerSecond,
            "Cycling did not return to the first scenario and its gameplay clock.");
        cycleRoute.Add(1);
        for (var index = 0; index < config.Scenarios.Count; index++)
        {
            if (config.Scenarios[index].DesktopEdgeEntry)
            {
                scenarioComboBox.SelectedIndex = 0;
                scenarioComboBox.SelectedIndex = index;
                Require(engine.Scenario == config.Scenarios[index],
                    $"The scenario selector did not start careful-step sequence {index + 1}.");
            }
            else StartScenario(index);
            playing = false;
            Require(PrinceFullyOffStage() && (engine.Scenario.DesktopEdgeEntry || engine.ClipName == "run"),
                $"Scenario {index + 1} did not start its configured entrance from fully offscreen.");
            if (engine.StartDelayTicksRemaining > 0)
            {
                UpdateDisplay();
                Require(phaseStatusLabel.Text == engine.Phase && engine.ElapsedTicks == 0,
                    $"Scenario {index + 1} did not display its entrance wait.");
                SavePreview(Path.Combine(directory, $"preview-{index + 1}-lead-in.png"));
            }
            if (engine.Scenario.DesktopEdgeEntry)
            {
                var prefix = $"preview-{index + 1}";
                var selectedScenario = engine.Scenario;
                var initialX = engine.X;
                var initialFrame = engine.Frame;
                var entryDirection = engine.Direction;
                var entryStep = engine.StepIndex;
                AdvanceUntil(PrinceVisibleOnStage, $"Scenario {index + 1} did not step into view.");
                Require(engine.StepIndex == entryStep && engine.Direction == entryDirection,
                    $"Scenario {index + 1} did not enter during its forward careful step.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-entry.png"));
                AdvanceUntil(() => engine.StepIndex != entryStep, $"Scenario {index + 1} did not reach its standing hold.");
                var holdStepIndex = engine.StepIndex;
                var holdStep = selectedScenario.Steps[holdStepIndex];
                var holdClip = config.Clips[holdStep.Clip];
                Require(holdStep.UntilX == null && holdClip.ExitDx == 0 && !holdClip.ExitFlip &&
                    holdClip.Frames.All(f => f.Dx == 0 && !f.Flip) && PrinceVisibleOnStage(),
                    $"Scenario {index + 1} did not hold a visible stationary pose.");
                var expectedHoldTicks = holdClip.Frames.Skip(holdStep.StartFrameIndex).Sum(f => f.Ticks) +
                    (holdStep.Repeat - 1) * holdClip.Frames.Sum(f => f.Ticks);
                var holdStartTick = engine.ElapsedTicks;
                var holdX = engine.X;
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-hold.png"));
                SavePreview(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}-{index + 1}.png"));
                while (engine.StepIndex == holdStepIndex && !engine.Completed)
                {
                    Require(engine.X == holdX && engine.Direction == entryDirection,
                        $"Scenario {index + 1} moved or turned during its standing hold.");
                    engine.Tick();
                }
                Require(!engine.Completed && engine.ElapsedTicks - holdStartTick == expectedHoldTicks,
                    $"Scenario {index + 1} did not retain the full configured standing duration.");
                var reverseStep = engine.StepIndex;
                AdvanceUntil(() => entryDirection * (engine.X - holdX) < 0,
                    $"Scenario {index + 1} did not begin backing toward its entry edge.");
                Require(engine.StepIndex == reverseStep && engine.Direction == entryDirection && PrinceVisibleOnStage(),
                    $"Scenario {index + 1} did not reverse its movement while retaining its facing.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-reverse.png"));
                while (!engine.Completed)
                {
                    Require(engine.Direction == entryDirection && entryDirection * (engine.X - holdX) <= 0,
                        $"Scenario {index + 1} turned or advanced while backing offscreen.");
                    engine.Tick();
                }
                Require(PrinceFullyOffStage() && engine.Direction == entryDirection,
                    $"Scenario {index + 1} did not finish offscreen with its original facing.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-final.png"));
                Require(playButton.Text == "Replay", $"Scenario {index + 1} did not offer replay after backing offscreen.");
                playButton.PerformClick();
                Require(playing && engine.Scenario == selectedScenario && engine.ElapsedTicks == 0 &&
                    engine.StartDelayTicksRemaining == selectedScenario.StartDelayTicks && engine.X == initialX &&
                    engine.Frame == initialFrame && engine.Direction == entryDirection && PrinceFullyOffStage(),
                    $"Replay did not restore scenario {index + 1}'s careful entrance and delay.");
                continue;
            }
            if (engine.Scenario.Id is "potion-direct-return-right" or "potion-direct-return-left")
            {
                var entryDirection = engine.Direction;
                var prefix = $"preview-{index + 1}";
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-potion-bubbling.png"));
                // Find the approach boundary before advancing the displayed actor.
                var probe = new AnimationEngine(config, engine.Scenario);
                long lastRunTick = 0;
                while (probe.ClipName == "run" && !probe.Completed && probe.ElapsedTicks < 10000)
                {
                    lastRunTick = probe.ElapsedTicks;
                    probe.Tick();
                }
                Require(probe.ClipName == "shortStop", $"Scenario {index + 1} did not brake before drinking.");
                AdvanceUntil(() => engine.ElapsedTicks == lastRunTick, $"Scenario {index + 1}'s approach frame was not reached.");
                Require(engine.ClipName == "run" && PrinceFullyOnStage() && engine.PotionVisible,
                    $"Scenario {index + 1} did not approach the potion while running.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-approach.png"));
                engine.Tick();
                var brakingFrames = new List<int>();
                while (engine.ClipName == "shortStop" && !engine.Completed)
                {
                    Require(engine.PotionVisible && engine.Direction == entryDirection,
                        $"Scenario {index + 1} picked up its potion or turned during braking.");
                    brakingFrames.Add(engine.Frame);
                    if (engine.Frame == 52)
                    {
                        UpdateDisplay();
                        SavePreview(Path.Combine(directory, $"{prefix}-last-stop.png"));
                    }
                    engine.Tick();
                }
                Require(brakingFrames.SequenceEqual([53, 54, 55, 56, 49, 50, 51, 52]),
                    $"Scenario {index + 1} did not retain all eight stopping poses through pose 52.");
                Require(engine.ClipName == "shortStand" && engine.Frame == 15 && engine.Direction == entryDirection && engine.PotionVisible && !engine.PotionConsumed,
                    $"Scenario {index + 1} did not stand for one tick before drinking.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-before-drink.png"));
                engine.Tick();
                Require(engine.ClipName == "drink" && engine.Direction == entryDirection && !engine.PotionVisible && !engine.PotionConsumed,
                    $"Scenario {index + 1} did not drink immediately after its one-tick standing pose.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-first-drink.png"));
                for (var i = 0; i < 12; i++) engine.Tick();
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-drink.png"));
                SavePreview(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}-{index + 1}.png"));
                AdvanceUntil(() => engine.ClipName == "shortStandingTurn", $"Scenario {index + 1} did not turn after drinking.");
                var turningFrames = new List<int>();
                while (engine.ClipName == "shortStandingTurn" && !engine.Completed)
                {
                    Require(engine.Direction == -entryDirection && engine.PotionConsumed && !engine.PotionVisible,
                        $"Scenario {index + 1} did not turn after consuming its potion.");
                    turningFrames.Add(engine.Frame);
                    if (turningFrames.Count == 5)
                    {
                        UpdateDisplay();
                        SavePreview(Path.Combine(directory, $"{prefix}-turn.png"));
                    }
                    if (engine.Frame == 51)
                    {
                        UpdateDisplay();
                        SavePreview(Path.Combine(directory, $"{prefix}-last-turn.png"));
                    }
                    engine.Tick();
                }
                Require(turningFrames.SequenceEqual([45, 46, 47, 48, 49, 50, 51]) && engine.ClipName == "start",
                    $"Scenario {index + 1} did not skip pose 52 and enter its running start immediately.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-first-start.png"));
                AdvanceUntil(() => engine.ClipName == "run", $"Scenario {index + 1} did not depart after drinking and turning.");
                Require(engine.Direction == -entryDirection && engine.PotionConsumed && !engine.PotionVisible,
                    $"Scenario {index + 1} did not run back toward its entry edge.");
                for (var i = 0; i < 6; i++) engine.Tick();
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"{prefix}-departure.png"));
                continue;
            }
            if (engine.Scenario.Actors.Count > 0)
            {
                var actor = engine.SupportingActors.Single();
                var waitingStep = actor.StepIndex;
                AdvanceUntil(() => actor.StepIndex != waitingStep,
                    "The Princess did not respond after the Prince's offscreen approach.");
                Require(PrinceFullyOnStage(), "The greeting approach did not show the Prince fully onstage.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"preview-{index + 1}-approach.png"));
                AdvanceUntil(() => !engine.PrimaryVisible, "Greeting did not switch to the combined reunion sprites.");
                Require(actor.HidesPrimaryActor && config.Clips[actor.ClipName].Frames.Any(f => f.Frame == actor.Frame && f.HidePrimaryActor),
                    "The greeting handoff did not occur on its configured combined-sprite event.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"preview-{index + 1}-contact.png"));
                AdvanceUntil(() => engine.Completed, "The greeting did not reach its final embrace.");
                var finalActorStep = actor.Scenario.Steps[^1];
                Require(!engine.PrimaryVisible && actor.Completed && actor.Frame == config.Clips[finalActorStep.Clip].Frames[^1].Frame,
                    "The completed greeting did not retain its configured final reunion sprite.");
                UpdateDisplay();
                SavePreview(Path.Combine(directory, $"preview-{index + 1}-final.png"));
                SavePreview(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}-{index + 1}.png"));
                continue;
            }
            var desired = engine.Scenario.Steps.Any(s => s.Clip == "skidTurn") ? "skidTurn" : "run";
            var guard = 0;
            while (engine.ClipName != desired && !engine.Completed && guard++ < 10000) engine.Tick();
            if (desired == "run") AdvanceUntil(PrinceFullyOnStage, $"Scenario {index + 1} did not run into view.");
            for (var i = 0; i < (desired == "skidTurn" ? 8 : 6); i++) engine.Tick();
            UpdateDisplay();
            var name = index == 0 ? path : Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}-{index + 1}.png");
            SavePreview(name);
        }
        Size = MinimumSize;
        guidesCheckBox.Checked = true;
        SavePreview(Path.Combine(directory, "preview-minimum-guides.png"));
        File.WriteAllText(Path.Combine(directory, "ui-smoke-checks.txt"),
            $"PASS (Windows Forms): native controls, pause, one-tick step, configured entrance delays/lead-in captures/restart with ambient potion bubbles, potion bubbles/reset/drinking renders, scenario selection, speed, guides, embedded defaults reload, failed settings/missing embedded image preserves playback, read-only embedded JSON viewer, {config.Scenarios.Count} offscreen entries/scenario renders, greeting shortcuts/restart/clock, mirrored greeting selection/restart/replay, potion shortcuts/restart/braking/standing-to-drink boundaries/turn-to-start boundaries/return departures, cycling {string.Join("->", cycleRoute)}, both greeting approaches/combined-sprite handoffs/final embraces, careful-step selection/entry/configured stationary hold/backing/final offscreen/replay in both directions, minimum window layout.\n");
    }

    private void SavePreview(string path)
    {
        PerformLayout();
        Update();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        bitmap.Save(path, ImageFormat.Png);
    }
}
