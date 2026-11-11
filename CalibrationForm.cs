namespace PenPulse;

internal sealed class CalibrationForm : Form
{
    private readonly Settings draft;
    private readonly IReadOnlyList<DrawingTexture> textures;
    private readonly Action<Settings, Gesture?> preview;
    private readonly Action stop;
    private readonly Label heading = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 14, FontStyle.Bold)
    };
    private readonly Label description = new()
    {
        Dock = DockStyle.Fill
    };
    private readonly FlowLayoutPanel choices = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true
    };
    private readonly Button back = new()
    {
        Text = "Back",
        Width = 90
    };
    private readonly Button next = new()
    {
        Text = "Next",
        Width = 110
    };
    private int step;
    public Settings Result => draft;

    public CalibrationForm(Settings initial, IReadOnlyList<DrawingTexture> textures, Action<Settings, Gesture?> preview, Action stop)
    {
        draft = initial.CloneFeedback();
        this.textures = textures;
        this.preview = preview;
        this.stop = stop;
        Text = "Calibrate PenPulse";
        ClientSize = new Size(580, 410);
        MinimumSize = new Size(540, 400);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(heading);
        layout.Controls.Add(description);
        layout.Controls.Add(choices);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var cancel = new Button
        {
            Text = "Cancel",
            Width = 90,
            DialogResult = DialogResult.Cancel
        };
        actions.Controls.Add(next);
        actions.Controls.Add(back);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = cancel;
        back.Click += (_, _) =>
        {
            stop();
            step--;
            ShowStep();
        };
        next.Click += (_, _) =>
        {
            stop();
            if (step == 2)
            {
                draft.Normalize();
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                step++;
                ShowStep();
            }
        };
        FormClosed += (_, _) => stop();
        ShowStep();
    }

    private void ShowStep()
    {
        foreach (Control child in choices.Controls.Cast<Control>().ToArray())
            child.Dispose();
        back.Enabled = step > 0;
        next.Text = step == 2 ? "Apply" : "Next";
        heading.Text = $"Step {step + 1} of 3 — " + (step == 0 ? "Overall strength" : step == 1 ? "Double-click spacing" : "Drawing feel");
        description.Text = step == 2 ? "Choose a supported texture, then hold the pen near or on the screen and try an 0.8-second preview. Only Apply changes your default settings." : "Select a setting, then try it while holding the connected pen near the screen. Next keeps your choice; Cancel leaves your settings unchanged.";
        if (step == 0)
        {
            AddOption("Gentle — 40%", Math.Abs(draft.Intensity - .4) < .01, () => draft.Intensity = .4);
            AddOption("Balanced — 70%", Math.Abs(draft.Intensity - .7) < .01, () => draft.Intensity = .7);
            AddOption("Strong — 100%", Math.Abs(draft.Intensity - 1) < .01, () => draft.Intensity = 1);
            choices.Controls.Add(new Label { Text = $"Current: {draft.Intensity:P0}. Your existing value stays until you choose another.", AutoSize = true });
        }
        else if (step == 1)
        {
            foreach (int gap in new[]
            {
                150,
                250,
                350
            }

            )
                AddOption(gap + " ms between pulses", draft.TopDoubleClick.GapMilliseconds == gap, () => draft.TopDoubleClick.GapMilliseconds = gap);
        }
        else
        {
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 240
            };
            box.Items.AddRange(textures.Cast<object>().ToArray());
            box.SelectedItem = textures.Contains(draft.DrawingTexture) ? draft.DrawingTexture : DrawingTexture.Pulses;
            draft.DrawingTexture = (DrawingTexture)box.SelectedItem!;
            box.SelectedIndexChanged += (_, _) =>
            {
                stop();
                draft.DrawingTexture = (DrawingTexture)box.SelectedItem!;
            };
            choices.Controls.Add(box);
            var strength = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                Increment = 5,
                Value = (decimal)Math.Round(draft.DrawingStrength * 100),
                Width = 100
            };
            choices.Controls.Add(new Label { Text = "Drawing strength (%)", AutoSize = true });
            choices.Controls.Add(strength);
            strength.ValueChanged += (_, _) => draft.DrawingStrength = (double)strength.Value / 100;
        }

        var test = new Button
        {
            Text = step == 2 ? "Play 0.8-second preview" : "Try this setting",
            AutoSize = true,
            Height = 34
        };
        test.Click += (_, _) =>
        {
            var sample = draft.CloneFeedback();
            if (step == 0)
            {
                sample.Tap.Enabled = true;
                sample.Tap.Strength = 1;
                sample.Tap.PulseCount = 1;
            }

            if (step == 1)
            {
                sample.TopDoubleClick.Enabled = true;
                sample.TopDoubleClick.PulseCount = 2;
                sample.TopDoubleClick.Strength = 1;
            }

            preview(sample, step == 0 ? Gesture.Press : step == 1 ? Gesture.TopDoubleClick : null);
        };
        choices.Controls.Add(test);
    }

    private void AddOption(string text, bool selected, Action choose)
    {
        var option = new RadioButton
        {
            Text = text,
            Checked = selected,
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 6)
        };
        option.CheckedChanged += (_, _) =>
        {
            if (option.Checked)
            {
                stop();
                choose();
            }
        };
        choices.Controls.Add(option);
    }
}
