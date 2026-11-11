using System.Text.Json;
using PenPulse;
using PenPulse.Testing;

internal static class FeatureTests
{
    private static void Equal<T>(T actual, T expected)
    {
        if (!Equals(actual, expected))
            throw new Exception($"Expected {expected}; got {actual}");
    }

    private static void Near(double actual, double expected)
    {
        if (Math.Abs(actual - expected) > .00001)
            throw new Exception($"Expected {expected}; got {actual}");
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or JsonException)
        {
            return;
        }

        throw new Exception("Invalid input was accepted.");
    }

    private static Haptics Playback(Action<Settings>? configure = null, bool texture = false)
    {
        Hardware.Reset();
        Clock.TickCount64 = 1000;
        Hardware.SupportsTextures = texture;
        var settings = new Settings
        {
            Intensity = 1,
            DrawingStrength = 1
        };
        configure?.Invoke(settings);
        settings.Normalize();
        var haptics = new Haptics();
        haptics.Configure(settings);
        Equal(haptics.Connect(1), true);
        return haptics;
    }

    private static void Tick(long time)
    {
        Clock.TickCount64 = time;
        foreach (var timer in System.Windows.Forms.Timer.Instances.ToArray())
            timer.Fire();
    }

    public static void Run(Action<string, Action> check)
    {
        check("Saved profiles are independent snapshots and exclude local exclusions", () =>
        {
            var source = new Settings
            {
                ExcludedApps = "PrivateApp"
            };
            var library = new ProfileLibrary();
            var saved = library.Add("Writing", source);
            source.Tap.Strength = .12;
            saved.Feedback.Hold.Strength = .25;
            Equal(saved.Feedback.Tap.Strength, 1d);
            Equal(source.Hold.Strength, 1d);
            Equal(saved.Feedback.ExcludedApps, "");
        });
        check("App profiles match exact names ignoring case and exe suffix", () =>
        {
            var library = new ProfileLibrary();
            var saved = library.Add("Notes", new());
            library.AppProfiles["OneNote.EXE"] = saved.Id;
            library.Normalize();
            Equal(library.Resolve("onenote")?.Id, saved.Id);
            Equal(library.Resolve("ONENOTE.exe")?.Id, saved.Id);
            Equal(library.Resolve("OtherOneNote"), null);
            Equal(ProfileLibrary.ProcessName("Company.Editor.exe"), "company.editor");
            library.AutomaticSwitching = false;
            Equal(library.Resolve("OneNote"), null);
        });
        check("Deleting a profile removes its assignments only", () =>
        {
            var library = new ProfileLibrary();
            var a = library.Add("A", new());
            var b = library.Add("B", new());
            library.AppProfiles["a"] = a.Id;
            library.AppProfiles["b"] = b.Id;
            library.Remove(a.Id);
            Equal(library.AppProfiles.ContainsKey("a"), false);
            Equal(library.Resolve("b")?.Id, b.Id);
        });
        check("Profile import round-trips custom effects without IDs or app rules", () =>
        {
            var saved = new SavedProfile
            {
                Name = "Custom",
                Feedback = new Settings
                {
                    PressureFeedback = true,
                    PressureSensitivity = 2,
                    DrawingTexture = DrawingTexture.Ink
                }
            };
            saved.Feedback.TopDoubleClick.GapMilliseconds = 280;
            var imported = ProfileTransfer.Import(ProfileTransfer.Export(saved));
            Equal(imported.Name, "Custom");
            Equal(imported.Feedback.PressureSensitivity, 2d);
            Equal(imported.Feedback.TopDoubleClick.GapMilliseconds, 280);
            Equal(imported.Id == saved.Id, false);
            Equal(imported.Feedback.DrawingTexture, DrawingTexture.Ink);
        });
        check("Using an imported profile preserves this PC's exclusions", () =>
        {
            var settings = new Settings
            {
                ExcludedApps = "PrivateApp"
            };
            var saved = new SavedProfile
            {
                Name = "Test",
                Feedback = new Settings
                {
                    ExcludedApps = "OtherApp",
                    Intensity = .3
                }
            };
            settings.CopyFeedbackFrom(ProfileTransfer.Import(ProfileTransfer.Export(saved)).Feedback);
            Equal(settings.ExcludedApps, "PrivateApp");
            Equal(settings.Intensity, .3);
        });
        check("Import rejects arbitrary JSON and unsupported profile versions", () =>
        {
            Reject(() => ProfileTransfer.Import("{}"));
            Reject(() => ProfileTransfer.Import("[]"));
            Reject(() => ProfileTransfer.Import("{\"Format\":\"PenPulseProfile\",\"Version\":2,\"Name\":\"A\",\"Feedback\":{}}"));
            Reject(() => ProfileTransfer.Import("{\"Format\":\"PenPulseProfile\",\"Version\":1,\"Name\":\"A\",\"Feedback\":null}"));
            Reject(() => ProfileTransfer.Import(new string(' ', ProfileTransfer.MaximumBytes + 1)));
        });
        check("Profile names and app mappings reject unsafe or ambiguous values", () =>
        {
            var library = new ProfileLibrary();
            library.Add("Writing", new());
            Reject(() => library.Add("writing", new()));
            Reject(() => library.Add("\n", new()));
            Reject(() => ProfileLibrary.ProcessName("C:\\Apps\\OneNote.exe"));
            Reject(() => ProfileLibrary.ProcessName("a;b"));
            library.Profiles.Add(new SavedProfile { Id = library.Profiles[0].Id, Name = "Duplicate ID" });
            Reject(library.Normalize);
        });
        check("Library persistence preserves previous data and reports malformed files", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "PenPulse-FeatureTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "profiles.json");
            try
            {
                var library = new ProfileLibrary();
                library.Add("First", new());
                LocalJson.Save(path, library);
                library.Add("Second", new());
                LocalJson.Save(path, library);
                Equal(LocalJson.Load<ProfileLibrary>(path, out string error).Profiles.Count, 2);
                Equal(error, "");
                Equal(LocalJson.Load<ProfileLibrary>(path + ".bak", out _).Profiles.Count, 1);
                File.WriteAllText(path, "broken");
                LocalJson.Load<ProfileLibrary>(path, out error);
                Equal(error.Length > 0, true);
                Equal(File.ReadAllText(path), "broken");
            }
            finally
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                    File.Delete(file);
                Directory.Delete(directory);
            }
        });
        check("Older preferences retain pulse mode with pressure disabled", () =>
        {
            var settings = JsonSerializer.Deserialize<Settings>("{\"SchemaVersion\":1,\"Intensity\":0.9}")!;
            settings.Normalize();
            Equal(settings.DrawingTexture, DrawingTexture.Pulses);
            Equal(settings.PressureFeedback, false);
            Equal(settings.Intensity, .9);
        });
        check("New feedback values are normalized before use", () =>
        {
            var settings = new Settings
            {
                DrawingTexture = (DrawingTexture)999,
                PressureSensitivity = double.NaN,
                MinimumPressureStrength = 4
            };
            settings.Normalize();
            Equal(settings.DrawingTexture, DrawingTexture.Pulses);
            Equal(settings.PressureSensitivity, 1d);
            Equal(settings.MinimumPressureStrength, 1d);
        });
        check("Timed pause expires once and manual pause stays indefinite", () =>
        {
            var pause = new PauseState();
            pause.ForMinutes(1000, 10);
            Equal(pause.Tick(600999), false);
            Equal(pause.IsPaused, true);
            Equal(pause.Tick(601000), true);
            Equal(pause.IsPaused, false);
            Equal(pause.Tick(601001), false);
            pause.ForMinutes(1000, 5);
            pause.Set(true);
            Equal(pause.Until, null);
            Equal(pause.Tick(9999999), false);
            pause.Set(false);
            Equal(pause.IsPaused, false);
        });
        check("Reconnect backs off and fresh pen input bypasses only the long wait", () =>
        {
            var retry = new ReconnectSchedule();
            retry.Reset(1000);
            Equal(retry.TryTake(1000, false), true);
            Equal(retry.TryTake(1200, true), false);
            Equal(retry.TryTake(1500, false), true);
            Equal(retry.TryTake(2000, false), false);
            Equal(retry.TryTake(2000, true), true);
            Equal(retry.TryTake(2100, true), false);
            retry.Reset(2200);
            Equal(retry.Attempts, 0);
            Equal(retry.TryTake(2200, false), true);
        });
        check("Reconnect eventually waits thirty seconds between idle attempts", () =>
        {
            var retry = new ReconnectSchedule();
            retry.Reset(0);
            foreach (long time in new long[]
            {
                0,
                500,
                1500,
                3500,
                8500,
                18500
            }

            )
                Equal(retry.TryTake(time, false), true);
            Equal(retry.TryTake(48499, false), false);
            Equal(retry.TryTake(48500, false), true);
        });
        check("Pressure decoding respects device range and signed values", () =>
        {
            Equal(PressureResponse.Normalize(2048, 0, 4096, 16), .5);
            Equal(PressureResponse.Normalize(0, 0, 0, 16), null);
            Equal(PressureResponse.Normalize(5000, 0, 4096, 16), null);
            Equal(PressureResponse.Normalize(0xff, -128, 127, 8), 127 / 255d);
        });
        check("Pressure curve is bounded and unknown pressure keeps normal strength", () =>
        {
            Near(PressureResponse.Multiplier(0, true, 1, .15), .15);
            Near(PressureResponse.Multiplier(1, true, 1, .15), 1);
            Near(PressureResponse.Multiplier(.5, true, 1, .2), .6);
            Near(PressureResponse.Multiplier(null, true, 1, .2), 1);
            Near(PressureResponse.Multiplier(double.NaN, true, 1, .2), 1);
            Near(PressureResponse.Multiplier(.1, false, 1, .2), 1);
            Equal(PressureResponse.Multiplier(.5, true, 2, 0) > PressureResponse.Multiplier(.5, true, 1, 0), true);
        });
        check("Shortcut configuration avoids reserved keys and weak modifier combinations", () =>
        {
            var options = new DesktopOptions
            {
                PauseShortcutKey = 0x7b,
                PauseShortcutModifiers = 8
            };
            options.Normalize();
            Equal(options.PauseShortcutKey, 0x50u);
            Equal(options.PauseShortcutModifiers, 3u);
            options.PauseShortcutKey = 0x7a;
            options.PauseShortcutModifiers = 7;
            options.Normalize();
            Equal(options.PauseShortcutKey, 0x7au);
        });
        check("Unsupported drawing textures use the existing click fallback", () =>
        {
            using var h = Playback(s => s.DrawingTexture = DrawingTexture.Ink);
            h.SetDrawing(true);
            Equal(Hardware.Effects[0].Waveform, (ushort)4099);
            Equal(h.TextureStatus.Contains("pulses"), true);
        });
        check("Continuous drawing always uses bounded duration and refreshes with motion", () =>
        {
            using var h = Playback(s => s.DrawingTexture = DrawingTexture.Ink, true);
            h.SetDrawing(true);
            Equal(Hardware.Effects.Count, 1);
            Equal(Hardware.Effects[0].Duration, 120);
            Tick(1079);
            Equal(Hardware.Effects.Count, 1);
            h.SetDrawing(true);
            Tick(1080);
            Equal(Hardware.Effects.Count, 2);
            Equal(Hardware.Effects.All(e => e.Duration == 120), true);
        });
        check("Lifting or expiring motion stops a continuous texture", () =>
        {
            using var h = Playback(s => s.DrawingTexture = DrawingTexture.Ink, true);
            h.SetDrawing(true);
            h.SetDrawing(false);
            Equal(Hardware.Stops > 0, true);
            int count = Hardware.Effects.Count;
            Tick(2000);
            Equal(Hardware.Effects.Count, count);
            h.SetDrawing(true);
            Tick(2200);
            Equal(h.DrawingActive, false);
            Equal(Hardware.Stops >= 2, true);
        });
        check("Rejected duration playback falls back without losing the pen connection", () =>
        {
            using var h = Playback(s => s.DrawingTexture = DrawingTexture.Ink, true);
            Hardware.RejectDuration = true;
            h.SetDrawing(true);
            Equal(h.Connected, true);
            Equal(Hardware.Effects.Last().Waveform, (ushort)4099);
            Equal(h.TextureStatus.Contains("declined"), true);
        });
        check("Pressure changes drawing strength but not discrete gesture strength", () =>
        {
            using var h = Playback(s =>
            {
                s.PressureFeedback = true;
                s.MinimumPressureStrength = .2;
                s.Tap.Strength = .8;
            });
            h.SetDrawing(true, .5);
            Near(Hardware.Sends.Last().Strength, .6);
            h.Play(Gesture.Press);
            Near(Hardware.Sends.Last().Strength, .8);
        });
        check("Pens without intensity control use normal drawing strength", () =>
        {
            using var h = Playback(s => s.PressureFeedback = true);
            h.Disconnect();
            Hardware.SupportsIntensity = false;
            h.Connect(1);
            h.SetDrawing(true, 0);
            Near(Hardware.Sends.Last().Strength, 1);
        });
        check("Profile changes cancel active textures and queued old effects", () =>
        {
            using var h = Playback(s => s.DrawingTexture = DrawingTexture.Ink, true);
            h.SetDrawing(true);
            h.Play(Gesture.TopDoubleClick);
            h.Configure(new Settings());
            int count = Hardware.Sends.Count;
            Tick(5000);
            Equal(Hardware.Sends.Count, count);
            Equal(h.DrawingActive, false);
        });
        check("Connection recovery succeeds after a temporarily missing controller", () =>
        {
            Hardware.Reset();
            using var h = new Haptics();
            Hardware.FailConnection = true;
            Equal(h.Connect(1), false);
            Hardware.FailConnection = false;
            Equal(h.Connect(1), true);
            h.Disconnect();
            Equal(h.Connected, false);
            Equal(h.Connect(1), true);
        });
        check("Automatic profile switches preserve the top-button sequence's original feel", () =>
        {
            using var h = Playback();
            h.Play(Gesture.TopDoubleClick);
            Tick(1035);
            h.Configure(new Settings { Intensity = .1 }, preserveShortcut: true);
            Tick(1350);
            Equal(Hardware.Sends.Count, 2);
            Equal(Hardware.Sends[1].Time, 1350L);
            Near(Hardware.Sends[1].Strength, 1);
        });
        check("A profile switch during the playback permission check discards old contact pulses", () =>
        {
            using var h = Playback();
            h.Play(Gesture.RightClick);
            bool switched = false;
            h.PlaybackAllowed = () =>
            {
                if (!switched)
                {
                    switched = true;
                    h.Configure(new Settings());
                }

                return true;
            };
            Tick(2000);
            Equal(Hardware.Sends.Count, 1);
        });
        check("Diagnostics explain blocking states and avoid raw input content", () =>
        {
            var d = new DiagnosticSnapshot("0.9.0", true, true, false, false, false, 1, true, .6, "Default settings", 10, 100, false, true, "Pulses", "Ready", "Off", "PenDevice", "Accepted", "");
            Equal(d.Explanation.Contains("paused"), true);
            Equal((d with
            {
                Paused = false,
                OverallStrength = 0
            }).Explanation.Contains("zero"), true);
            Equal((d with
            {
                Paused = false,
                Connected = false
            }).Explanation.Contains("blue pad"), true);
            Equal(d.Report().Contains("No automatic upload"), true);
        });
    }
}
