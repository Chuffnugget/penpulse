using PenPulse;

internal static class Program
{
    private static int passed, failed;
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Check("Configuration reload updates pressure, texture and gesture controls", () =>
        {
            var settings = new Settings();
            using var host = Host();
            var tabs = new ConfigurationPanel(settings, () =>
            {
            }, _ =>
            {
            }, new CheckBox(), 500);
            host.Controls.Add(tabs);
            host.Show();
            settings.PressureFeedback = true;
            settings.PressureSensitivity = 1.75;
            settings.DrawingTexture = DrawingTexture.Ink;
            settings.DrawingFeedback = false;
            settings.HoldMilliseconds = 800;
            tabs.Reload();
            Equal(Find<CheckBox>(tabs, "Vary drawing strength with pen pressure").Checked, true);
            Equal(Find<NumericUpDown>(tabs, "Pressure sensitivity").Value, 1.75m);
            Equal((DrawingTexture)Find<ComboBox>(tabs, "Drawing texture").SelectedItem!, DrawingTexture.Ink);
            Equal(Find<CheckBox>(tabs, "Enable drawing / dragging feedback").Checked, false);
            Equal(Find<NumericUpDown>(tabs, "Long press after").Value, 800m);
        });
        Check("Profile controls save snapshots and restore them into defaults", () =>
        {
            var defaults = new Settings();
            var library = new ProfileLibrary();
            int saves = 0, applied = 0;
            using var host = Host();
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };
            host.Controls.Add(tabs);
            var panels = new FeaturePanels(defaults, library, new DesktopOptions(), () => saves++, () => applied++, () =>
            {
            }, Diagnostic, () =>
            {
            });
            panels.AddTo(tabs);
            host.Show();
            Find<TextBox>(tabs, "Profile name").Text = "Writing";
            Find<Button>(tabs, "Save as new").PerformClick();
            Equal(library.Profiles.Count, 1);
            Equal(saves, 1);
            defaults.Intensity = .2;
            Find<Button>(tabs, "Use as default").PerformClick();
            Equal(defaults.Intensity, 1d);
            Equal(applied, 1);
        });
        Check("Feature pages remain scrollable at the normal window size", () =>
        {
            using var host = Host();
            host.ClientSize = new Size(710, 450);
            var tabs = new ConfigurationPanel(new(), () =>
            {
            }, _ =>
            {
            }, new CheckBox(), 500);
            host.Controls.Add(tabs);
            new FeaturePanels(new(), new(), new(), () =>
            {
            }, () =>
            {
            }, () =>
            {
            }, Diagnostic, () =>
            {
            }).AddTo(tabs);
            host.Show();
            Application.DoEvents();
            foreach (int index in new[]
            {
                0,
                3,
                4
            }

            )
            {
                tabs.SelectedIndex = index;
                tabs.SelectedTab!.PerformLayout();
                Application.DoEvents();
                Equal(tabs.SelectedTab.VerticalScroll.Visible, true);
            }
        });
        Check("Wrapped feature controls fit their rows at narrow and normal widths", () =>
        {
            using var host = Host();
            var tabs = new TabControl { Dock = DockStyle.Fill };
            host.Controls.Add(tabs);
            new FeaturePanels(new(), new(), new(), () => { }, () => { }, () => { }, Diagnostic, () => { }).AddTo(tabs);
            host.Show();
            foreach (int width in new[] { 650, 710, 900 })
            {
                host.ClientSize = new Size(width, 450);
                foreach (TabPage page in tabs.TabPages)
                {
                    tabs.SelectedTab = page;
                    page.PerformLayout();
                    Application.DoEvents();
                    foreach (var row in All(page).OfType<FlowLayoutPanel>())
                        foreach (Control child in row.Controls)
                            if (child.Bottom > row.ClientSize.Height || child.Right > row.ClientSize.Width)
                                throw new Exception($"{child.Text} is clipped at width {width}: {child.Bounds} in {row.ClientSize}.");
                }
            }
        });
        Check("App assignment controls create a working exact-name mapping", () =>
        {
            var library = new ProfileLibrary();
            var profile = library.Add("Notes", new());
            using var host = Host();
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };
            host.Controls.Add(tabs);
            new FeaturePanels(new(), library, new(), () =>
            {
            }, () =>
            {
            }, () =>
            {
            }, Diagnostic, () =>
            {
            }).AddTo(tabs);
            host.Show();
            Find<TextBox>(tabs, "App executable").Text = "OneNote.EXE";
            Find<Button>(tabs, "Assign").PerformClick();
            Equal(library.Resolve("onenote")?.Id, profile.Id);
        });
        Check("Shortcut controls change only desktop options", () =>
        {
            var options = new DesktopOptions();
            var settings = new Settings();
            int changed = 0;
            using var host = Host();
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };
            host.Controls.Add(tabs);
            new FeaturePanels(settings, new(), options, () =>
            {
            }, () =>
            {
            }, () => changed++, Diagnostic, () =>
            {
            }).AddTo(tabs);
            host.Show();
            tabs.SelectedIndex = 1;
            Find<CheckBox>(tabs, "Enable a keyboard shortcut to pause / resume").Checked = true;
            Equal(options.PauseShortcutEnabled, true);
            Equal(changed, 1);
            Equal(settings.Intensity, 1d);
        });
        Check("Troubleshooting screen displays the current blocking reason", () =>
        {
            using var host = Host();
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };
            host.Controls.Add(tabs);
            var panels = new FeaturePanels(new(), new(), new(), () =>
            {
            }, () =>
            {
            }, () =>
            {
            }, Diagnostic, () =>
            {
            });
            panels.AddTo(tabs);
            panels.UpdateDiagnostics(Diagnostic());
            Equal(All(tabs).OfType<Label>().Any(l => l.Text.Contains("Feedback is paused")), true);
            Equal(All(tabs).OfType<TextBox>().Any(t => t.Text.Contains("No automatic upload")), true);
        });
        Check("Calibration changes remain in its draft until the caller applies them", () =>
        {
            var initial = new Settings { Intensity = .7 };
            int stopped = 0;
            using var wizard = new CalibrationForm(initial, [DrawingTexture.Pulses, DrawingTexture.Ink], (_, _) =>
            {
            }, () => stopped++);
            wizard.Opacity = 0;
            wizard.Show();
            Find<RadioButton>(wizard, "Strong — 100%").Checked = true;
            Equal(wizard.Result.Intensity, 1d);
            Equal(initial.Intensity, .7);
            wizard.Close();
            Equal(stopped > 0, true);
        });
        Check("Calibration offers only supported textures and a bounded preview request", () =>
        {
            Settings? sample = null;
            Gesture? action = Gesture.Press;
            using var wizard = new CalibrationForm(new(), [DrawingTexture.Pulses, DrawingTexture.Ink], (s, g) =>
            {
                sample = s;
                action = g;
            }, () =>
            {
            });
            wizard.Opacity = 0;
            wizard.Show();
            Find<Button>(wizard, "Next").PerformClick();
            Find<Button>(wizard, "Next").PerformClick();
            var choices = All(wizard).OfType<ComboBox>().Single();
            Equal(choices.Items.Count, 2);
            choices.SelectedItem = DrawingTexture.Ink;
            Find<Button>(wizard, "Play 0.8-second preview").PerformClick();
            Equal(sample?.DrawingTexture, DrawingTexture.Ink);
            Equal(action, null);
        });
        Console.WriteLine($"{passed}/{passed + failed} Windows control checks passed. No pen input, startup registry or real settings were changed.");
        return failed == 0 ? 0 : 1;
    }

    // These tests host the actual controls in an invisible, isolated form. They do
    // not instantiate MainWindow, register input listeners or touch user settings.
    private static Form Host() => new()
    {
        AutoScaleMode = AutoScaleMode.None,
        Font = new Font("Segoe UI", 10),
        ClientSize = new Size(900, 900),
        ShowInTaskbar = false,
        Opacity = 0
    };
    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var next in All(child))
                yield return next;
        }
    }

    private static T Find<T>(Control parent, string name)
        where T : Control => All(parent).OfType<T>().First(c => c.Text == name || c.AccessibleName == name);
    private static void Equal<T>(T actual, T expected)
    {
        if (!Equals(actual, expected))
            throw new Exception($"Expected {expected}; got {actual}");
    }

    private static void Check(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }

    private static DiagnosticSnapshot Diagnostic() => new("test", true, true, false, false, false, 1, true, .6, "Default settings", 0, -1, false, true, "Pulses", "Ready", "Off", "Fake", "Fake", "");
}
