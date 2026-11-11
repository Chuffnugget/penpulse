namespace PenPulse;

internal sealed class FeaturePanels
{
    private readonly Settings defaults;
    private readonly ProfileLibrary library;
    private readonly DesktopOptions options;
    private readonly Action saveProfiles, defaultsChanged, optionsChanged;
    private readonly Func<DiagnosticSnapshot> diagnose;
    private readonly Action calibrate;
    private readonly ListBox profiles = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "Saved profiles"
    };
    private readonly ListBox rules = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "App assignments"
    };
    private readonly ComboBox ruleProfile = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 190,
        AccessibleName = "Assigned profile"
    };
    private readonly TextBox profileName = new()
    {
        Width = 230,
        MaxLength = 64,
        PlaceholderText = "Profile name",
        AccessibleName = "Profile name"
    };
    private readonly TextBox appName = new()
    {
        Width = 200,
        MaxLength = 124,
        PlaceholderText = "App name, e.g. OneNote.exe",
        AccessibleName = "App executable"
    };
    private readonly TextBox report = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window
    };
    private readonly Label issue = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(24, 91, 137)
    };
    private readonly Label shortcuts = new()
    {
        Dock = DockStyle.Fill
    };
    private bool refreshing;
    public FeaturePanels(Settings defaults, ProfileLibrary library, DesktopOptions options, Action saveProfiles, Action defaultsChanged, Action optionsChanged, Func<DiagnosticSnapshot> diagnose, Action calibrate)
    {
        this.defaults = defaults;
        this.library = library;
        this.options = options;
        this.saveProfiles = saveProfiles;
        this.defaultsChanged = defaultsChanged;
        this.optionsChanged = optionsChanged;
        this.diagnose = diagnose;
        this.calibrate = calibrate;
    }

    public void AddTo(TabControl tabs)
    {
        tabs.TabPages.Add(BuildProfiles());
        tabs.TabPages.Add(BuildTools());
        RefreshProfiles();
    }

    private TabPage BuildProfiles()
    {
        var page = Page("Profiles", out var stack);
        Add(stack, new Label { Text = "Drawing and click controls edit your default settings. Save a snapshot, then assign it to an app.", Dock = DockStyle.Fill }, 44);
        Add(stack, profiles, 110);
        var naming = Row();
        naming.Controls.Add(profileName);
        naming.Controls.Add(Button("Save as new", () =>
        {
            library.Add(profileName.Text, defaults);
            Persist();
        }));
        Add(stack, naming, 38);
        var actions = Row();
        actions.Controls.Add(Button("Use as default", () =>
        {
            defaults.CopyFeedbackFrom(Selected().Feedback);
            defaultsChanged();
        }));
        actions.Controls.Add(Button("Replace saved", () =>
        {
            var selected = Selected();
            selected.Feedback = defaults.CloneFeedback();
            Persist();
        }));
        actions.Controls.Add(Button("Delete", () =>
        {
            library.Remove(Selected().Id);
            Persist();
        }));
        actions.Controls.Add(Button("Import…", Import));
        actions.Controls.Add(Button("Export…", Export));
        Add(stack, actions, 74);
        var automatic = new CheckBox
        {
            Text = "Switch profiles automatically for these apps",
            Checked = library.AutomaticSwitching,
            AutoSize = true
        };
        automatic.CheckedChanged += (_, _) => Run(() =>
        {
            library.AutomaticSwitching = automatic.Checked;
            Persist();
        });
        Add(stack, automatic, 32);
        Add(stack, rules, 100);
        var mapping = Row();
        mapping.Controls.Add(appName);
        mapping.Controls.Add(ruleProfile);
        mapping.Controls.Add(Button("Assign", () =>
        {
            if (ruleProfile.SelectedItem is not SavedProfile profile)
                throw new InvalidOperationException("Save or import a profile first.");
            string name = ProfileLibrary.ProcessName(appName.Text);
            if (library.AppProfiles.Count >= 256 && !library.AppProfiles.ContainsKey(name))
                throw new InvalidOperationException("Too many app assignments.");
            library.AppProfiles[name] = profile.Id;
            Persist();
        }));
        Add(stack, mapping, 70);
        var remove = Row();
        remove.Controls.Add(Button("Remove assignment", () =>
        {
            if (rules.SelectedItem is not RuleRow row)
                return;
            library.AppProfiles.Remove(row.App);
            Persist();
        }));
        Add(stack, remove, 36);
        Add(stack, new Label { Text = "Unassigned apps use your default settings. Global app exclusions still apply to every profile.", Dock = DockStyle.Fill }, 44);
        profiles.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshing && profiles.SelectedItem is SavedProfile p)
                profileName.Text = p.Name;
        };
        return page;
    }

    private TabPage BuildTools()
    {
        var page = Page("Tools", out var stack);
        var reconnect = new CheckBox
        {
            Text = "Reconnect automatically after sleep or connection loss",
            Checked = options.AutoReconnect,
            AutoSize = true
        };
        reconnect.CheckedChanged += (_, _) => Run(() =>
        {
            options.AutoReconnect = reconnect.Checked;
            optionsChanged();
        });
        Add(stack, reconnect, 34);
        var hotkey = new CheckBox
        {
            Text = "Enable a keyboard shortcut to pause / resume",
            Checked = options.PauseShortcutEnabled,
            AutoSize = true
        };
        Add(stack, hotkey, 32);
        var modifier = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 160
        };
        var modifiers = new[]
        {
            new Choice("Ctrl + Alt", 3),
            new Choice("Ctrl + Shift", 6),
            new Choice("Alt + Shift", 5),
            new Choice("Ctrl + Alt + Shift", 7)
        };
        modifier.Items.AddRange(modifiers);
        modifier.SelectedItem = modifiers.FirstOrDefault(m => m.Value == options.PauseShortcutModifiers) ?? modifiers[0];
        var key = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90
        };
        var keys = Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).Concat(Enumerable.Range(0x70, 11)).Select(k => new Choice(k >= 0x70 ? "F" + (k - 0x70 + 1) : ((char)k).ToString(), (uint)k)).ToArray();
        key.Items.AddRange(keys);
        key.SelectedItem = keys.First(k => k.Value == options.PauseShortcutKey);
        void CommitShortcut()
        {
            options.PauseShortcutEnabled = hotkey.Checked;
            options.PauseShortcutModifiers = ((Choice)modifier.SelectedItem!).Value;
            options.PauseShortcutKey = ((Choice)key.SelectedItem!).Value;
            modifier.Enabled = key.Enabled = hotkey.Checked;
            optionsChanged();
        }

        modifier.Enabled = key.Enabled = hotkey.Checked;
        hotkey.CheckedChanged += (_, _) => Run(CommitShortcut);
        modifier.SelectedIndexChanged += (_, _) => Run(CommitShortcut);
        key.SelectedIndexChanged += (_, _) => Run(CommitShortcut);
        var hotkeyRow = Row();
        hotkeyRow.Controls.Add(modifier);
        hotkeyRow.Controls.Add(key);
        Add(stack, hotkeyRow, 38);
        Add(stack, shortcuts, 44);
        var calibration = Row();
        calibration.Controls.Add(Button("Calibrate feedback…", calibrate));
        Add(stack, calibration, 40);
        Add(stack, new Label { Text = "Why isn't it vibrating?", Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Dock = DockStyle.Fill }, 32);
        Add(stack, issue, 58);
        Add(stack, report, 190);
        var copy = Row();
        copy.Controls.Add(Button("Copy troubleshooting report", () => Clipboard.SetText(diagnose().Report())));
        Add(stack, copy, 40);
        return page;
    }

    public void UpdateDiagnostics(DiagnosticSnapshot snapshot)
    {
        issue.Text = snapshot.Explanation;
        shortcuts.Text = snapshot.Shortcut;
        string next = snapshot.Report();
        if (report.Text != next && !report.Focused)
            report.Text = next;
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PenPulse profile (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        if (new FileInfo(dialog.FileName).Length > ProfileTransfer.MaximumBytes)
            throw new InvalidDataException("Profile files must be smaller than 1 MB.");
        var imported = ProfileTransfer.Import(File.ReadAllText(dialog.FileName));
        string original = imported.Name, name = original;
        for (int n = 2; library.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
            name = original[..Math.Min(original.Length, 55)] + " (" + n + ")";
        library.Add(name, imported.Feedback);
        Persist();
    }

    private void Export()
    {
        var selected = Selected();
        string safe = string.Concat(selected.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        using var dialog = new SaveFileDialog
        {
            Filter = "PenPulse profile (*.json)|*.json",
            FileName = safe + ".json",
            DefaultExt = "json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            File.WriteAllText(dialog.FileName, ProfileTransfer.Export(selected));
    }

    private SavedProfile Selected() => profiles.SelectedItem as SavedProfile ?? throw new InvalidOperationException("Select a saved profile first.");
    private void Persist()
    {
        library.Normalize();
        saveProfiles();
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        refreshing = true;
        string? selected = (profiles.SelectedItem as SavedProfile)?.Id;
        profiles.Items.Clear();
        ruleProfile.Items.Clear();
        rules.Items.Clear();
        profiles.Items.AddRange(library.Profiles.ToArray());
        ruleProfile.Items.AddRange(library.Profiles.ToArray());
        profiles.SelectedItem = library.Profiles.Find(p => p.Id == selected);
        if (profiles.SelectedIndex < 0 && profiles.Items.Count > 0)
            profiles.SelectedIndex = 0;
        if (ruleProfile.Items.Count > 0)
            ruleProfile.SelectedIndex = 0;
        foreach (var pair in library.AppProfiles.OrderBy(p => p.Key))
            rules.Items.Add(new RuleRow(pair.Key, library.Profiles.Find(p => p.Id == pair.Value)?.Name ?? "Missing profile"));
        refreshing = false;
    }

    private static TabPage Page(string title, out TableLayoutPanel stack)
    {
        var page = new TabPage(title)
        {
            AutoScroll = true,
            Padding = new Padding(12),
            BackColor = Color.White
        };
        stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(stack);
        return page;
    }

    private static FlowLayoutPanel Row() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true
    };
    private static void Add(TableLayoutPanel parent, Control child, int height)
    {
        int row = parent.RowCount++;
        // Button heights follow the display font. Let wrapped rows grow so their
        // second line remains reachable on high-DPI or narrow windows.
        parent.RowStyles.Add(child is FlowLayoutPanel
            ? new RowStyle(SizeType.AutoSize)
            : new RowStyle(SizeType.Absolute, height));
        parent.Controls.Add(child, 0, row);
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 30,
            MinimumSize = new Size(80, 30)
        };
        button.Click += (_, _) => Run(action);
        return button;
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "PenPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private sealed record Choice(string Name, uint Value)
    {
        public override string ToString() => Name;
    }

    private sealed record RuleRow(string App, string Profile)
    {
        public override string ToString() => App + ".exe → " + Profile;
    }
}
