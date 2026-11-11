namespace PenPulse;

internal sealed class ConfigurationPanel : TabControl
{
    private readonly Settings settings;
    private readonly Action changed;
    private readonly Action<Gesture> preview;
    private bool loading = true, connected, paused;
    private readonly Gesture[] actions = [Gesture.Press, Gesture.Hold, Gesture.RightClick, Gesture.DoubleTap, Gesture.Eraser, Gesture.Drag, Gesture.Release, Gesture.TopClick, Gesture.TopDoubleClick, Gesture.TopHold];
    private readonly ListBox actionList = new();
    private readonly Button previewButton = new()
    {
        Text = "Try this effect",
        Height = 36,
        MinimumSize = new Size(160, 30),
        Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(229, 239, 255),
        ForeColor = Color.FromArgb(32, 85, 167)
    };
    private readonly Label actionHelp = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.DimGray
    };
    private TrackBar drawingStrength = null!, drawingSpeed = null!, masterStrength = null!, effectStrength = null!;
    private CheckBox drawingEnabled = null!, movingOnly = null!, effectEnabled = null!, alternate = null!, systemDoubleTap = null!;
    private NumericUpDown stopDelay = null!, movement = null!, pulseCount = null!, pulseGap = null!, holdDelay = null!, doubleTapDelay = null!;
    private NumericUpDown pressureSensitivity = null!, pressureMinimum = null!;
    private CheckBox pressureEnabled = null!;
    private ComboBox textureChoice = null!;
    private EffectSettings Current => settings.For(actions[Math.Max(0, actionList.SelectedIndex)]);

    public ConfigurationPanel(Settings settings, Action changed, Action<Gesture> preview, CheckBox startup, int windowsDoubleTap)
    {
        this.settings = settings;
        this.changed = changed;
        this.preview = preview;
        Dock = DockStyle.Fill;
        Padding = new Point(16, 6);
        AccessibleName = "Feedback settings";
        var drawing = Page("Drawing & dragging");
        var ds = Stack(drawing);
        drawingEnabled = Check(ds, "Enable drawing / dragging feedback", settings.DrawingFeedback, v => settings.DrawingFeedback = v);
        drawingStrength = Slider(ds, "Drawing strength", 0, 100, (int)Math.Round(settings.DrawingStrength * 100), v => v + "%", v => settings.DrawingStrength = v / 100d);
        drawingSpeed = Slider(ds, "Pulse speed", 2, 20, (int)Math.Round(1000d / settings.DrawingIntervalMilliseconds), v => "~" + v + " / sec", v => settings.DrawingIntervalMilliseconds = (int)Math.Round(1000d / v));
        TextRow(ds, "Higher speed makes the pulses closer together. Drawing strength is separate from tap and hold strength.", 44);
        movingOnly = Check(ds, "Only vibrate while the pen is moving", settings.DrawingOnlyWhenMoving, v => settings.DrawingOnlyWhenMoving = v);
        movement = Number(ds, "Movement needed", 1, 10, (decimal)settings.DrawingMovementThreshold, "pixels · lower is more sensitive", v => settings.DrawingMovementThreshold = (double)v);
        stopDelay = Number(ds, "Stop when still for", 50, 300, settings.DrawingStopMilliseconds, "milliseconds", v => settings.DrawingStopMilliseconds = (int)v, 10);
        TextRow(ds, "Lifting the pen always stops movement feedback. Turn off “Only vibrate while moving” to also feel pulses while holding the tip still.", 52);
        TextRow(ds, "Drawing texture (experimental; unsupported textures fall back to pulses)", 42);
        textureChoice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            AccessibleName = "Drawing texture"
        };
        textureChoice.Items.AddRange(Enum.GetValues<DrawingTexture>().Cast<object>().ToArray());
        textureChoice.SelectedItem = settings.DrawingTexture;
        textureChoice.SelectedIndexChanged += (_, _) =>
        {
            if (!loading && textureChoice.SelectedItem is DrawingTexture texture)
            {
                settings.DrawingTexture = texture;
                Commit();
            }
        };
        Add(ds, textureChoice, 36);
        pressureEnabled = Check(ds, "Vary drawing strength with pen pressure", settings.PressureFeedback, v => settings.PressureFeedback = v);
        pressureSensitivity = Number(ds, "Pressure sensitivity", .25m, 3, (decimal)settings.PressureSensitivity, "higher feels stronger sooner", v => settings.PressureSensitivity = (double)v, .25m);
        pressureSensitivity.DecimalPlaces = 2;
        pressureMinimum = Number(ds, "Light-touch strength", 0, 100, (decimal)Math.Round(settings.MinimumPressureStrength * 100), "% of drawing strength", v => settings.MinimumPressureStrength = (double)v / 100, 5);
        TextRow(ds, "Pressure affects drawing only. If pressure or intensity control is unavailable, normal drawing strength is used. Tools shows the current capabilities.", 60);
        var effects = Page("Taps, holds & clicks");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        effects.Controls.Add(grid);
        actionList.Dock = DockStyle.Fill;
        actionList.IntegralHeight = false;
        actionList.AccessibleName = "Action to customize";
        actionList.Items.AddRange(actions.Select(Settings.ActionName).Cast<object>().ToArray());
        grid.Controls.Add(actionList, 0, 0);
        var detail = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(12, 0, 0, 0)
        };
        grid.Controls.Add(detail, 1, 0);
        var es = Stack(detail);
        effectEnabled = Check(es, "Enable this effect", true, v => Current.Enabled = v);
        effectStrength = Slider(es, "Effect strength", 0, 100, 60, v => v + "%", v => Current.Strength = v / 100d, 115);
        pulseCount = Number(es, "Number of pulses", 1, 6, 1, "", v => Current.PulseCount = (int)v, labelWidth: 150);
        pulseGap = Number(es, "Time between pulses", 50, 400, 85, "milliseconds", v => Current.GapMilliseconds = (int)v, 5, 150);
        alternate = Check(es, "Alternate softer and stronger pulses", false, v => Current.AlternatePulses = v);
        Add(es, previewButton, 42);
        previewButton.Click += (_, _) => this.preview(actions[actionList.SelectedIndex]);
        Add(es, actionHelp, 66);
        actionList.SelectedIndexChanged += (_, _) => LoadEffect();
        actionList.SelectedIndex = 0;
        var general = Page("General");
        var gs = Stack(general);
        masterStrength = Slider(gs, "Overall strength", 0, 100, (int)Math.Round(settings.Intensity * 100), v => v + "%", v => settings.Intensity = v / 100d);
        TextRow(gs, "Each action's strength is scaled by overall strength. Set overall strength to 100% to use the full range.", 40);
        var presets = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = System.Windows.Forms.Padding.Empty
        };
        foreach (var name in new[]
        {
            "Soft",
            "Balanced",
            "Strong"
        }

        )
        {
            var button = new Button
            {
                Text = name,
                Width = 105,
                Height = 32,
                AccessibleName = name + " preset"
            };
            button.Click += (_, _) =>
            {
                settings.ApplyPreset(name);
                RefreshValues();
                Commit();
            };
            presets.Controls.Add(button);
        }

        Add(gs, presets, 40);
        TextRow(gs, "Presets change overall strength, drawing strength and drawing speed.", 28);
        holdDelay = Number(gs, "Long press after", 250, 1500, settings.HoldMilliseconds, "milliseconds", v => settings.HoldMilliseconds = (int)v, 50);
        systemDoubleTap = Check(gs, "Use Windows double-tap timing", settings.DoubleTapMilliseconds == 0, v =>
        {
            settings.DoubleTapMilliseconds = v ? 0 : (int)doubleTapDelay.Value;
            doubleTapDelay.Enabled = !v;
        });
        doubleTapDelay = Number(gs, "Double-tap window", 150, 1000, settings.DoubleTapMilliseconds == 0 ? Math.Clamp(windowsDoubleTap, 150, 1000) : settings.DoubleTapMilliseconds, "milliseconds", v =>
        {
            if (!systemDoubleTap.Checked)
                settings.DoubleTapMilliseconds = (int)v;
        }, 25);
        doubleTapDelay.Enabled = !systemDoubleTap.Checked;
        TextRow(gs, "Skip these apps (separate names with ; — for example OneNote; Photoshop)", 28);
        var excluded = new TextBox
        {
            Text = settings.ExcludedApps,
            Dock = DockStyle.Fill,
            AccessibleName = "Excluded apps"
        };
        excluded.TextChanged += (_, _) =>
        {
            if (!loading)
            {
                settings.ExcludedApps = excluded.Text;
                Commit();
            }
        };
        Add(gs, excluded, 34);
        Add(gs, startup, 34);
        TextRow(gs, "Starts in the tray when you sign in. After a restart or sleep, touch the blue pad if your pen needs to reconnect.", 48);
        TextRow(gs, "Closing or minimising this window keeps feedback running. Click the tray icon to reopen it, or choose Exit PenPulse to stop.", 48);
        loading = false;
        LoadEffect();
    }

    private TabPage Page(string title)
    {
        var p = new TabPage(title)
        {
            Padding = new Padding(12),
            BackColor = Color.White,
            AutoScroll = true
        };
        TabPages.Add(p);
        return p;
    }

    private static TableLayoutPanel Stack(Control parent)
    {
        var s = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = System.Windows.Forms.Padding.Empty
        };
        s.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        parent.Controls.Add(s);
        return s;
    }

    private static void Add(TableLayoutPanel stack, Control child, int height)
    {
        int row = stack.RowCount++;
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        stack.Controls.Add(child, 0, row);
    }

    private static void TextRow(TableLayoutPanel stack, string text, int height) => Add(stack, new Label { Text = text, Dock = DockStyle.Fill, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9) }, height);
    private CheckBox Check(TableLayoutPanel stack, string title, bool value, Action<bool> update)
    {
        var check = new CheckBox
        {
            Text = title,
            Checked = value,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AccessibleName = title
        };
        check.CheckedChanged += (_, _) =>
        {
            if (!loading)
            {
                update(check.Checked);
                Commit();
            }
        };
        Add(stack, check, 34);
        return check;
    }

    private TrackBar Slider(TableLayoutPanel stack, string title, int min, int max, int value, Func<int, string> format, Action<int> update, int labelWidth = 175)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = System.Windows.Forms.Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        row.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 5, 0, 0) });
        var slider = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TickStyle = TickStyle.None,
            AccessibleName = title
        };
        row.Controls.Add(slider);
        var label = new Label
        {
            Text = format(slider.Value),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(0, 5, 0, 0)
        };
        row.Controls.Add(label);
        slider.ValueChanged += (_, _) =>
        {
            label.Text = format(slider.Value);
            if (!loading)
            {
                update(slider.Value);
                Commit();
            }
        };
        Add(stack, row, 48);
        return slider;
    }

    private NumericUpDown Number(TableLayoutPanel stack, string title, decimal min, decimal max, decimal value, string unit, Action<decimal> update, decimal increment = 1, int labelWidth = 175)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = System.Windows.Forms.Padding.Empty,
            Padding = new Padding(0, 4, 0, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 3, 0, 0) });
        var number = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Increment = increment,
            Dock = DockStyle.Top,
            AccessibleName = title
        };
        row.Controls.Add(number);
        row.Controls.Add(new Label { Text = unit, Dock = DockStyle.Fill, Padding = new Padding(6, 3, 0, 0), ForeColor = Color.DimGray });
        number.ValueChanged += (_, _) =>
        {
            if (!loading)
            {
                update(number.Value);
                Commit();
            }
        };
        Add(stack, row, 42);
        return number;
    }

    private void Commit()
    {
        settings.Normalize();
        changed();
        UpdatePreview();
    }

    private void RefreshValues()
    {
        loading = true;
        drawingStrength.Value = (int)Math.Round(settings.DrawingStrength * 100);
        drawingSpeed.Value = Math.Clamp((int)Math.Round(1000d / settings.DrawingIntervalMilliseconds), 2, 20);
        masterStrength.Value = (int)Math.Round(settings.Intensity * 100);
        loading = false;
    }

    public void Reload()
    {
        RefreshValues();
        loading = true;
        drawingEnabled.Checked = settings.DrawingFeedback;
        movingOnly.Checked = settings.DrawingOnlyWhenMoving;
        movement.Value = (decimal)settings.DrawingMovementThreshold;
        stopDelay.Value = settings.DrawingStopMilliseconds;
        holdDelay.Value = settings.HoldMilliseconds;
        systemDoubleTap.Checked = settings.DoubleTapMilliseconds == 0;
        doubleTapDelay.Value = settings.DoubleTapMilliseconds == 0 ? Math.Clamp((int)Native.GetDoubleClickTime(), 150, 1000) : settings.DoubleTapMilliseconds;
        doubleTapDelay.Enabled = !systemDoubleTap.Checked;
        textureChoice.SelectedItem = settings.DrawingTexture;
        pressureEnabled.Checked = settings.PressureFeedback;
        pressureSensitivity.Value = (decimal)settings.PressureSensitivity;
        pressureMinimum.Value = (decimal)Math.Round(settings.MinimumPressureStrength * 100);
        LoadEffect();
        loading = false;
    }

    private void LoadEffect()
    {
        if (actionList.SelectedIndex < 0)
            return;
        bool wasLoading = loading;
        loading = true;
        effectEnabled.Checked = Current.Enabled;
        effectStrength.Value = (int)Math.Round(Current.Strength * 100);
        pulseCount.Value = Current.PulseCount;
        pulseGap.Value = Current.GapMilliseconds;
        alternate.Checked = Current.AlternatePulses;
        actionHelp.Text = actions[actionList.SelectedIndex] switch
        {
            Gesture.TopClick or Gesture.TopDoubleClick or Gesture.TopHold => "The clickable button on the eraser end, without touching the screen. Its usual Windows shortcut stays active. Connect on the blue pad first; away-from-screen feedback needs testing.",
            Gesture.Release => "Finish dragging plays when you lift after moving. Connect on the blue pad before trying an effect.",
            Gesture.DoubleTap => "When double-tap feedback is off, the second contact uses your normal tap effect.",
            _ => "Connect on the blue pad, then use Try this effect while holding the pen near the screen. Changes save automatically."
        };
        loading = wasLoading;
        UpdatePreview();
    }

    public void SetConnection(bool connected, bool paused)
    {
        this.connected = connected;
        this.paused = paused;
        UpdatePreview();
    }

    private void UpdatePreview() => previewButton.Enabled = connected && !paused && Current.Enabled && Current.Strength > 0 && settings.Intensity > 0;
}
