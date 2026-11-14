#nullable enable

namespace PopRunner.Sample;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private MenuStrip menuStrip = null!;
    private ComboBox scenarioComboBox = null!;
    private Label descriptionLabel = null!;
    private Label frameInfoLabel = null!;
    private Label timingLabel = null!;
    private StageView stageView = null!;
    private FrameStripView frameStripView = null!;
    private GroupBox framesGroupBox = null!;
    private Button playButton = null!;
    private Button restartButton = null!;
    private Button stepButton = null!;
    private Button viewConfigButton = null!;
    private Button reloadButton = null!;
    private NumericUpDown speedInput = null!;
    private CheckBox cycleCheckBox = null!;
    private CheckBox guidesCheckBox = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel configStatusLabel = null!;
    private ToolStripStatusLabel phaseStatusLabel = null!;
    private ToolStripStatusLabel elapsedStatusLabel = null!;
    private System.Windows.Forms.Timer renderTimer = null!;
    private ToolTip toolTip = null!;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        renderTimer = new System.Windows.Forms.Timer(components) { Interval = 15 };
        toolTip = new ToolTip(components);
        SuspendLayout();

        menuStrip = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&View configuration...", null, (_, _) => ViewConfig()));
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Load embedded defaults", null, (_, _) => ReloadConfig()) { ShortcutKeys = Keys.F5 });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        var playbackMenu = new ToolStripMenuItem("&Playback");
        playbackMenu.DropDownItems.Add(new ToolStripMenuItem("&Play / pause", null, (_, _) => TogglePlay()));
        playbackMenu.DropDownItems.Add(new ToolStripMenuItem("&Restart", null, (_, _) => Restart()));
        playbackMenu.DropDownItems.Add(new ToolStripMenuItem("&Step one tick", null, (_, _) => StepFrame()));
        menuStrip.Items.AddRange([fileMenu, playbackMenu]);

        scenarioComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DropDownWidth = 450 };
        descriptionLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
        timingLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Right, TextAlign = ContentAlignment.MiddleRight };
        viewConfigButton = new Button { Text = "View JSON...", AutoSize = true, MinimumSize = new Size(90, 28) };
        reloadButton = new Button { Text = "Load defaults (F5)", AutoSize = true, MinimumSize = new Size(90, 28) };
        var configButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(9, 0, 0, 0) };
        configButtons.Controls.AddRange([viewConfigButton, reloadButton]);

        var scenarioLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2, Padding = new Padding(6) };
        scenarioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scenarioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioLayout.Controls.Add(scenarioComboBox, 0, 0);
        scenarioLayout.Controls.Add(timingLabel, 1, 0);
        scenarioLayout.Controls.Add(configButtons, 2, 0);
        scenarioLayout.Controls.Add(descriptionLabel, 0, 1);
        scenarioLayout.SetColumnSpan(descriptionLabel, 3);
        var scenarioGroup = new GroupBox { Text = "Animation sequence", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6, 9, 6, 6) };
        scenarioGroup.Controls.Add(scenarioLayout);

        stageView = new StageView { Dock = DockStyle.Fill, Margin = new Padding(6) };
        var previewGroup = new GroupBox { Text = "Prince of Persia - preview", Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 10) };
        previewGroup.Controls.Add(stageView);

        frameInfoLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 5) };
        frameStripView = new FrameStripView { Dock = DockStyle.Fill };
        var framesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(3) };
        framesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        framesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        framesLayout.Controls.Add(frameInfoLabel, 0, 0);
        framesLayout.Controls.Add(frameStripView, 0, 1);
        framesGroupBox = new GroupBox { Text = "Sprite frames", Dock = DockStyle.Fill, Padding = new Padding(6, 8, 6, 6) };
        framesGroupBox.Controls.Add(framesLayout);

        playButton = new Button { Text = "Pause", AutoSize = true, MinimumSize = new Size(90, 30) };
        restartButton = new Button { Text = "Restart", AutoSize = true, MinimumSize = new Size(80, 30) };
        stepButton = new Button { Text = "Step one tick", AutoSize = true, MinimumSize = new Size(100, 30) };
        speedInput = new NumericUpDown { Minimum = 0.25m, Maximum = 2m, Value = 1m, Increment = 0.25m, DecimalPlaces = 2, Width = 65, Margin = new Padding(3, 7, 3, 3) };
        var speedLabel = new Label { Text = "Speed:", AutoSize = true, Margin = new Padding(16, 10, 0, 3) };
        var speedUnitLabel = new Label { Text = "x", AutoSize = true, Margin = new Padding(0, 10, 9, 3) };
        cycleCheckBox = new CheckBox { Text = "Cycle all sequences", AutoSize = true, Margin = new Padding(9, 9, 3, 3) };
        guidesCheckBox = new CheckBox { Text = "Alignment guides", AutoSize = true, Margin = new Padding(12, 9, 3, 3) };
        var playbackPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 4, 0, 0), WrapContents = true };
        playbackPanel.Controls.AddRange([playButton, restartButton, stepButton, speedLabel, speedInput, speedUnitLabel, cycleCheckBox, guidesCheckBox]);

        var rootLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 8), ColumnCount = 1, RowCount = 4 };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.Controls.Add(scenarioGroup, 0, 0);
        rootLayout.Controls.Add(previewGroup, 0, 1);
        rootLayout.Controls.Add(framesGroupBox, 0, 2);
        rootLayout.Controls.Add(playbackPanel, 0, 3);

        configStatusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        phaseStatusLabel = new ToolStripStatusLabel { BorderSides = ToolStripStatusLabelBorderSides.Left };
        elapsedStatusLabel = new ToolStripStatusLabel { BorderSides = ToolStripStatusLabelBorderSides.Left };
        statusStrip = new StatusStrip { ShowItemToolTips = true };
        statusStrip.Items.AddRange([configStatusLabel, phaseStatusLabel, elapsedStatusLabel]);
        Controls.Add(rootLayout);
        Controls.Add(statusStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        toolTip.SetToolTip(playButton, "Play / pause (Space)");
        toolTip.SetToolTip(restartButton, "Restart the selected sequence (R)");
        toolTip.SetToolTip(stepButton, "Pause and advance one DOS animation tick (Right arrow)");
        toolTip.SetToolTip(guidesCheckBox, "Show the original root position and sprite bounds (G)");
        toolTip.SetToolTip(scenarioComboBox, "Choose sequences 1-9 with their number keys; 0 selects sequence 10");
        toolTip.SetToolTip(viewConfigButton, "View the JSON embedded in PopRunner.Core; change its source and rebuild to modify it");
        toolTip.SetToolTip(reloadButton, "Load the embedded defaults and restart the selected sequence (F5)");

        scenarioComboBox.SelectedIndexChanged += (_, _) => ScenarioChanged();
        playButton.Click += (_, _) => TogglePlay();
        restartButton.Click += (_, _) => Restart();
        stepButton.Click += (_, _) => StepFrame();
        viewConfigButton.Click += (_, _) => ViewConfig();
        reloadButton.Click += (_, _) => ReloadConfig();
        guidesCheckBox.CheckedChanged += (_, _) => { stageView.ShowGuides = guidesCheckBox.Checked; stageView.Invalidate(); };
        renderTimer.Tick += RenderFrame;

        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(900, 650);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PopRunner.Sample - Prince of Persia (.NET 10 Windows Forms)";
        ResumeLayout(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            renderTimer?.Stop();
            components?.Dispose();
            atlas?.Dispose();
        }
        base.Dispose(disposing);
    }
}
