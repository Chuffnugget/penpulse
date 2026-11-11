using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace PenPulse;

internal sealed class MainWindow : Form
{
    // Give raw HID input a chance to arrive before using Windows' promoted mouse
    // event. These are fallback tolerances, not timings guaranteed by Windows.
    private const int MouseFallbackDelayMilliseconds = 30;
    private const int RawInputPriorityMilliseconds = 250;
    private const int PointerReconnectMilliseconds = 250;
    private const int AutomaticReconnectMilliseconds = 500;
    private const int DeviceAssociationMilliseconds = 200;
    private readonly Haptics haptics = new();
    private readonly PenContactState contact = new(() => (Native.GetAsyncKeyState(0x01) & 0x8000) != 0);
    private GestureRecognizer gestures => contact.Gestures;
    private StrokeMotion stroke => contact.Stroke;

    private readonly Settings settings = Settings.Load();
    private Settings activeSettings;
    private readonly ProfileLibrary profiles;
    private readonly DesktopOptions desktopOptions;
    private readonly FeaturePanels features;
    private readonly PauseState pauseState = new();
    private readonly ReconnectSchedule reconnectSchedule = new();
    private ShortcutRegistration? pauseShortcut;
    private bool paused => pauseState.IsPaused;

    private bool sessionLocked, powerSuspended, calibrating;
    private uint foregroundPid;
    private string? foregroundProcess;
    private string activeProfileId = "", activeProfileName = "Default settings", featureFault = "";
    private double? lastPressure;
    private bool pressureSeen;
    private long previewUntil;
    private readonly System.Windows.Forms.Timer runtimeTimer = new()
    {
        Interval = 200
    };
    private readonly System.Windows.Forms.Timer previewTimer = new()
    {
        Interval = 25
    };
    private static string ProfilesPath => Path.Combine(Settings.DataPath, "profiles.json");
    private static string DesktopOptionsPath => Path.Combine(Settings.DataPath, "desktop.json");

    private RawPenInput? raw;
    private readonly Native.HookProc hookProc;
    private nint hook, selectedRawDevice;
    private readonly System.Windows.Forms.Timer holdTimer = new()
    {
        Interval = 25
    };
    private readonly System.Windows.Forms.Timer statusTimer = new()
    {
        Interval = 1000
    };
    private readonly System.Windows.Forms.Timer fallbackTimer = new()
    {
        Interval = 35
    };
    private readonly Queue<(int Message, Native.Point Point, long Time)> fallback = new();
    private bool mouseContact, shuttingDown, initializing = true;
    private long recognized, lastStatusWrite, lastConnect = -10000, lastAutoConnect = -10000;
    private string lastGesture = "None yet", inputFault = "", saveFault = "";
    private long backgroundGestures, foregroundGestures, eraserGestures, ignoredEraserReports;
    private long topButtonGestures;
    private readonly Label connection = new(), live = new();
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem pauseMenu;
    private readonly ToolStripMenuItem startupMenu = new("Start with Windows")
    {
        CheckOnClick = true
    };
    private readonly CheckBox startWindows = new();
    private readonly Button pauseButton = new();
    private readonly PenPad pad;
    private readonly ConfigurationPanel configuration;
    private readonly Icon appIcon;
    public event Action? ExitRequested;
    public MainWindow()
    {
        profiles = LocalJson.Load<ProfileLibrary>(ProfilesPath, out var profilesError);
        try
        {
            profiles.Normalize();
        }
        catch (Exception e)
        {
            profiles = new ProfileLibrary();
            profilesError = e.Message;
        }

        desktopOptions = LocalJson.Load<DesktopOptions>(DesktopOptionsPath, out var desktopError);
        desktopOptions.Normalize();
        featureFault = string.Join(" ", new[] { profilesError, desktopError }.Where(s => s.Length > 0));
        activeSettings = settings;
        haptics.PlaybackAllowed = () =>
        {
            UpdateActiveProfile();
            return !paused && !sessionLocked && !powerSuspended && !IsExcluded() && (calibrating || ForegroundMatches());
        };
        contact.Expired += () =>
        {
            mouseContact = false;
            haptics.CancelContact();
        };
        SuspendLayout();
        Text = "PenPulse • Pen haptics";
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(760, 820);
        MinimumSize = new Size(720, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(247, 249, 252);
        appIcon = MakeIcon();
        Icon = appIcon;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 16, 22, 16),
            ColumnCount = 1,
            RowCount = 8
        };
        foreach (var height in new[]
        {
            44,
            30,
            100,
            36
        }

        )
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "PenPulse", Font = new Font("Segoe UI", 24, FontStyle.Bold), Dock = DockStyle.Fill, ForeColor = Color.FromArgb(25, 37, 55) });
        layout.Controls.Add(new Label { Text = "Fine-tune the feel of your pen. Changes save automatically.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(70, 83, 103) });
        pad = new PenPad();
        pad.Pointer += id => Connect(id);
        layout.Controls.Add(pad);
        connection.Dock = DockStyle.Fill;
        connection.Padding = new Padding(0, 5, 0, 0);
        connection.ForeColor = Color.FromArgb(24, 91, 137);
        layout.Controls.Add(connection);
        startWindows.Text = "Start with Windows";
        startWindows.AutoSize = true;
        startWindows.Checked = StartupEnabled();
        startupMenu.Checked = startWindows.Checked;
        startWindows.CheckedChanged += (_, _) => SetStartup(startWindows.Checked);
        settings.Normalize();
        configuration = new ConfigurationPanel(settings, ApplyConfiguration, g =>
        {
            UpdateActiveProfile(true);
            haptics.Play(g);
        }, startWindows, (int)Native.GetDoubleClickTime());
        features = new FeaturePanels(settings, profiles, desktopOptions, SaveProfiles, () =>
        {
            configuration.Reload();
            ApplyConfiguration();
        }, SaveDesktopOptions, GetDiagnosticSnapshot, Calibrate);
        features.AddTo(configuration);
        layout.Controls.Add(configuration);
        live.Dock = DockStyle.Fill;
        live.Font = new Font("Segoe UI", 9);
        live.ForeColor = Color.FromArgb(80, 91, 107);
        layout.Controls.Add(live);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var hide = new Button
        {
            Text = "Minimise to tray",
            Width = 180,
            Height = 36,
            BackColor = Color.FromArgb(32, 99, 211),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        hide.Click += (_, _) => MinimiseToTray();
        actions.Controls.Add(hide);
        pauseButton.Text = "Pause";
        pauseButton.Width = 90;
        pauseButton.Height = 36;
        pauseButton.Click += (_, _) => TogglePause();
        actions.Controls.Add(pauseButton);
        var reconnect = new Button
        {
            Text = "Reconnect",
            Width = 110,
            Height = 36
        };
        reconnect.Click += (_, _) => ResetConnection();
        actions.Controls.Add(reconnect);
        var exit = new Button
        {
            Text = "Exit PenPulse",
            Width = 125,
            Height = 36
        };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        actions.Controls.Add(exit);
        layout.Controls.Add(actions);
        // Controls are created in 96-DPI logical units. Scale their bounds exactly
        // once; point-based fonts are already rendered at the monitor's DPI.
        var dpiScale = DeviceDpi / 96f;
        Scale(new SizeF(dpiScale, dpiScale));
        ResumeLayout(true);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open PenPulse", null, (_, _) => ShowSettings());
        pauseMenu = new ToolStripMenuItem("Pause feedback", null, (_, _) => TogglePause());
        menu.Items.Add(pauseMenu);
        var timedPause = new ToolStripMenuItem("Pause for");
        foreach (int minutes in new[]
        {
            5,
            10,
            15,
            30,
            60
        }

        )
            timedPause.DropDownItems.Add(minutes + " minutes", null, (_, _) =>
            {
                pauseState.ForMinutes(Environment.TickCount64, minutes);
                PauseChanged();
            });
        menu.Items.Add(timedPause);
        menu.Items.Add("Reconnect pen", null, (_, _) =>
        {
            ResetConnection();
            ShowSettings();
        });
        menu.Items.Add("Open pen settings", null, (_, _) => Process.Start(new ProcessStartInfo("ms-settings:pen") { UseShellExecute = true }));
        startupMenu.Click += (_, _) => startWindows.Checked = startupMenu.Checked;
        menu.Items.Add(startupMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit PenPulse", null, (_, _) => ExitRequested?.Invoke());
        tray = new NotifyIcon
        {
            Icon = appIcon,
            Text = "PenPulse — connect your pen",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowSettings();
        };
        ApplyConfiguration();
        haptics.Changed += UpdateStatus;
        gestures.Recognized += OnGesture;
        _ = Handle;
        pauseShortcut = new ShortcutRegistration(Handle);
        pauseShortcut.Apply(desktopOptions);
        try
        {
            raw = new RawPenInput(Handle);
            raw.Sample += OnRawSample;
            raw.Shortcut += OnShortcut;
            raw.Fault += message => inputFault = message;
        }
        catch (Exception e)
        {
            inputFault = e.Message;
        }

        hookProc = MouseCallback;
        hook = Native.SetWindowsHookExW(14, hookProc, Native.GetModuleHandleW(null), 0);
        if (hook == 0)
            inputFault += " Pen mouse fallback unavailable: " + Marshal.GetLastWin32Error() + ".";
        holdTimer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            if (!paused)
            {
                contact.Tick(now);
                UpdateDrawing(now);
            }

            if (!gestures.Contact)
                holdTimer.Stop();
        };
        fallbackTimer.Tick += (_, _) => DrainFallback();
        statusTimer.Tick += (_, _) =>
        {
            TryReconnect();
            UpdateStatus();
        };
        statusTimer.Start();
        runtimeTimer.Tick += (_, _) =>
        {
            if (pauseState.Tick(Environment.TickCount64))
                PauseChanged();
            UpdateActiveProfile();
        };
        runtimeTimer.Start();
        previewTimer.Tick += (_, _) =>
        {
            if (!calibrating || paused || Environment.TickCount64 >= previewUntil)
                StopCalibrationPreview();
            else
                haptics.SetDrawing(true, .6);
        };
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        // Keep the message loop and input listeners alive when the window is hidden.
        // Explicit Exit and Windows shutdown must still be allowed to end the app.
        Resize += (_, _) =>
        {
            if (!shuttingDown && WindowState == FormWindowState.Minimized)
                MinimiseToTray();
        };
        FormClosing += (_, e) =>
        {
            if (!shuttingDown && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                MinimiseToTray();
            }
        };
        initializing = false;
        UpdateStatus();
    }

    private void Connect(uint id, bool playConnectionEffect = true, bool manual = false)
    {
        if (haptics.Connected || paused || sessionLocked || powerSuspended)
            return;
        var now = Environment.TickCount64;
        if (id == 0)
        {
            bool freshInput = raw != null && now - raw.LastReportAt < 500;
            if (!manual && (!desktopOptions.AutoReconnect || !reconnectSchedule.TryTake(now, freshInput)))
                return;
            if (now - lastAutoConnect < AutomaticReconnectMilliseconds)
                return;
            lastAutoConnect = now;
        }
        else
        {
            if (now - lastConnect < PointerReconnectMilliseconds)
                return;
            lastConnect = now;
        }

        if (haptics.Connect(id))
        {
            reconnectSchedule.Reset(now);
            UpdateActiveProfile();
            // Associate only recent raw input with the controller we just acquired.
            if (raw != null && Environment.TickCount64 - raw.LastReportAt < DeviceAssociationMilliseconds)
                selectedRawDevice = raw.LastDevice;
            if (playConnectionEffect)
                haptics.Play(Gesture.Press);
        }
    }

    private void OnRawSample(nint device, PenSample sample)
    {
        if (sample.Pressure.HasValue)
            pressureSeen = true;
        if (selectedRawDevice == 0 || selectedRawDevice == device)
            lastPressure = sample.Contact ? sample.Pressure : null;
        if (!haptics.Connected && !paused && sample.InRange)
            Connect(0, false);
        if (!haptics.Connected || paused || sessionLocked || powerSuspended || calibrating)
            return;
        UpdateActiveProfile();
        if (selectedRawDevice == 0)
            selectedRawDevice = device;
        if (selectedRawDevice != device)
        {
            if (sample.Eraser && sample.Contact)
                ignoredEraserReports++;
            return;
        }

        var now = Environment.TickCount64;
        contact.Feed(sample, now, raw: true);
        UpdateDrawing(now);
        if (gestures.Contact)
            holdTimer.Start();
        else
            holdTimer.Stop();
        if (!sample.InRange)
            haptics.CancelContact();
    }

    private void OnShortcut(Gesture gesture)
    {
        UpdateActiveProfile();
        if (paused || calibrating || sessionLocked || powerSuspended || IsExcluded() || !activeSettings.For(gesture).Enabled)
            return;
        if (!haptics.Connected)
            Connect(0, false);
        OnGesture(gesture);
    }

    private nint MouseCallback(int code, nint w, nint l)
    {
        // This callback only copies pen events into the UI queue; no disk, COM, or HID work.
        // Always pass the event onward, including if the utility encounters an error.
        try
        {
            if (code >= 0 && !shuttingDown)
            {
                var info = Marshal.PtrToStructure<Native.MouseHook>(l);
                int msg = (int)w;
                if (PenSignature.IsPen((ulong)info.Extra) && msg is 0x200 or 0x201 or 0x202 or 0x204 or 0x205)
                {
                    // Bound the queue under heavy motion; raw input is the preferred source.
                    if (fallback.Count < 256)
                        fallback.Enqueue((msg, info.Point, Environment.TickCount64));
                    fallbackTimer.Start();
                }
            }
        }
        catch
        {
        }

        return Native.CallNextHookEx(hook, code, w, l);
    }

    private void DrainFallback()
    {
        var now = Environment.TickCount64;
        UpdateActiveProfile();
        while (fallback.TryPeek(out var pending) && now - pending.Time >= MouseFallbackDelayMilliseconds)
        {
            fallback.Dequeue();
            if (paused || calibrating || sessionLocked || powerSuspended || !haptics.Connected)
                continue;
            var rawRecent = raw != null && now - raw.LastReportAt < RawInputPriorityMilliseconds;
            if (pending.Message == 0x204)
            {
                gestures.PromotedRightClick(now);
                continue;
            }

            if (rawRecent)
                continue;
            if (pending.Message == 0x201)
                mouseContact = true;
            if (pending.Message == 0x202)
                mouseContact = false;
            if (pending.Message is 0x201 or 0x202 || pending.Message == 0x200 && mouseContact)
            {
                lastPressure = null;
                var sample = new PenSample(mouseContact, false, false, true, pending.Point.X, pending.Point.Y);
                contact.Feed(sample, now, raw: false, newContact: pending.Message == 0x201);
                mouseContact = gestures.Contact;
                UpdateDrawing(now);
                if (gestures.Contact)
                    holdTimer.Start();
                else
                    holdTimer.Stop();
            }
        }

        if (fallback.Count == 0)
            fallbackTimer.Stop();
    }

    private void UpdateDrawing(long now) => haptics.SetDrawing(activeSettings.DrawingFeedback && !paused && !sessionLocked && !powerSuspended && !calibrating && (activeSettings.DrawingOnlyWhenMoving ? stroke.IsMoving(now) : stroke.IsContactRecent(now)) && !IsExcluded(), lastPressure);
    private void ApplyConfiguration()
    {
        settings.Normalize();
        UpdateActiveProfile(true);
        SaveSettings();
    }

    private void OnGesture(Gesture gesture)
    {
        if (paused || calibrating || sessionLocked || powerSuspended || IsExcluded())
            return;
        if (gesture == Gesture.DoubleTap && !activeSettings.DoubleTap.Enabled)
            gesture = Gesture.Press;
        if (!activeSettings.For(gesture).Enabled)
            return;
        if (gesture == Gesture.Eraser)
            eraserGestures++;
        if (PenShortcutRecognizer.IsShortcut(gesture))
            topButtonGestures++;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var foregroundPid);
        if (foregroundPid == Environment.ProcessId)
            foregroundGestures++;
        else
            backgroundGestures++;
        recognized++;
        lastGesture = Settings.ActionName(gesture);
        haptics.Play(gesture);
    }

    private bool IsExcluded()
    {
        if (string.IsNullOrWhiteSpace(settings.ExcludedApps))
            return false;
        try
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return settings.ExcludedApps.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(n => string.Equals(ProfileLibrary.ProcessName(n), ProfileLibrary.ProcessName(process.ProcessName), StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return true;
        } // Respect exclusions when foreground app identity cannot be read.
    }

    private void ResetGesture()
    {
        contact.Reset();
        raw?.ResetShortcuts();
        mouseContact = false;
        holdTimer.Stop();
        fallback.Clear();
        fallbackTimer.Stop();
        haptics.Cancel();
    }

    private void ResetConnection()
    {
        ResetConnectionState();
        Connect(0, false, true);
    }

    private void ResetConnectionState()
    {
        ResetGesture();
        selectedRawDevice = 0;
        lastPressure = null;
        reconnectSchedule.Reset(Environment.TickCount64);
        haptics.Disconnect();
    }

    private void TogglePause()
    {
        pauseState.Set(!paused);
        PauseChanged();
    }

    private void PauseChanged()
    {
        ResetGesture();
        pauseButton.Text = paused ? "Resume" : "Pause";
        pauseMenu.Text = paused ? "Resume feedback" : "Pause feedback";
        UpdateStatus();
    }

    private void MinimiseToTray()
    {
        Hide();
    }

    public void ShowSettings()
    {
        // Restore first: showing a still-minimised window would immediately hide it again.
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }

    protected override void WndProc(ref Message m)
    {
        try
        {
            if (m.Msg == ShortcutRegistration.Message && (int)m.WParam == ShortcutRegistration.Id)
                TogglePause();
            else if (m.Msg == Native.WM_INPUT)
                raw?.Read(m.LParam);
            else if (m.Msg == Native.WM_INPUT_DEVICE_CHANGE)
            {
                raw?.DeviceChanged(m.LParam);
                if (m.WParam == 2 && selectedRawDevice == m.LParam)
                    ResetConnectionState();
                else if (m.WParam == 1)
                    reconnectSchedule.Reset(Environment.TickCount64);
            }
            else if (m.Msg == 0x11 || m.Msg == 0x16)
                ResetGesture();
        }
        catch (Exception e)
        {
            inputFault = e.Message;
            ResetGesture();
        }

        base.WndProc(ref m); // Required raw-input cleanup; never consume another app's input.
    }

    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
            PostState(() =>
            {
                sessionLocked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff;
                ResetConnectionState();
            });
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
            PostState(() =>
            {
                powerSuspended = e.Mode == PowerModes.Suspend;
                ResetConnectionState();
            });
    }

    private void PostState(Action update)
    {
        try
        {
            if (!IsDisposed)
                BeginInvoke(() =>
                {
                    if (!shuttingDown)
                        update();
                });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SaveSettings()
    {
        if (initializing)
            return;
        try
        {
            settings.Save();
            saveFault = "";
        }
        catch (Exception e)
        {
            saveFault = "Settings could not be saved: " + e.Message;
        }
    }

    private static string StartupCommand => "\"" + Environment.ProcessPath + "\" --silent";

    private static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return string.Equals(key?.GetValue("PenPulse") as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
    }

    private void SetStartup(bool enabled)
    {
        if (initializing)
            return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
                key.SetValue("PenPulse", StartupCommand);
            else if (string.Equals(key.GetValue("PenPulse") as string, StartupCommand, StringComparison.OrdinalIgnoreCase))
                key.DeleteValue("PenPulse", false); // Another portable copy may now own this entry.
            startupMenu.Checked = startWindows.Checked = StartupEnabled();
        }
        catch (Exception e)
        {
            MessageBox.Show("Could not change Windows startup.\n" + e.Message, "PenPulse");
            initializing = true;
            startupMenu.Checked = startWindows.Checked = StartupEnabled();
            initializing = false;
        }
    }

    private void UpdateStatus()
    {
        if (shuttingDown)
            return;
        connection.Text = paused ? "Paused — pen input continues normally." : haptics.State;
        pad.Connected = haptics.Connected;
        pad.Invalidate();
        tray.Text = paused ? "PenPulse — paused" : haptics.Connected ? "PenPulse — background feedback active" : "PenPulse — open to connect your pen";
        configuration.SetConnection(haptics.Connected, paused);
        features?.UpdateDiagnostics(GetDiagnosticSnapshot());
        live.Text = $"{activeProfileName} · Last action: {lastGesture}. {(pauseState.Until is long until ? "Resumes in " + Math.Max(1, (until - Environment.TickCount64 + 59999) / 60000) + " min." : "")}\n{(string.IsNullOrEmpty(inputFault) ? haptics.TextureStatus : inputFault)}";
        if (!string.IsNullOrEmpty(saveFault))
            live.Text = saveFault;
        else if (!string.IsNullOrEmpty(featureFault))
            live.Text = "Profiles or desktop settings: " + featureFault;
        var now = Environment.TickCount64;
        if (now - lastStatusWrite < 5000)
            return;
        lastStatusWrite = now;
        try
        {
            Directory.CreateDirectory(Settings.DataPath);
            var status = new
            {
                version = Application.ProductVersion.Split('+')[0] + "-experimental",
                running = true,
                processId = Environment.ProcessId,
                updatedUtc = DateTimeOffset.UtcNow,
                connected = haptics.Connected,
                paused,
                activeProfile = activeProfileName,
                pressureSeen,
                texture = haptics.TextureStatus,
                rawReports = raw?.Reports ?? 0,
                gestures = recognized,
                lastGesture,
                hapticCommands = haptics.Commands,
                hardwareFeedbackCannotBeVerifiedAutomatically = true,
                state = haptics.State,
                inputFault,
                connectionAttempts = haptics.ConnectionAttempts,
                connectionStage = haptics.ConnectionStage,
                connectionMethod = haptics.ConnectionMethod,
                desktopHaptics = haptics.DesktopDiagnostic,
                supportedWaveforms = haptics.SupportedWaveforms,
                routingDiagnostic = haptics.RoutingDiagnostic,
                routedCommands = haptics.RoutedCommands,
                rejectedCommands = haptics.RejectedCommands,
                foregroundGestures,
                backgroundGestures,
                rawEraserContacts = raw?.EraserContactReports ?? 0,
                rawEraserHover = raw?.EraserHoverReports ?? 0,
                eraserGestures,
                ignoredEraserReports,
                topButtonKeyReports = raw?.ShortcutKeyReports ?? 0,
                topButtonShortcuts = raw?.Shortcuts ?? 0,
                topButtonGestures,
                penButtonReleaseReports = raw?.PenShortcutReleaseReports ?? 0,
                drawingEnabled = activeSettings.DrawingFeedback,
                drawingAvailable = haptics.DrawingAvailable,
                drawingActive = haptics.DrawingActive,
                drawingStarts = haptics.DrawingStarts,
                drawingMode = haptics.TextureStatus,
                drawingRequestedTexture = activeSettings.DrawingTexture.ToString(),
                pressureFeedback = activeSettings.PressureFeedback,
                drawingPulses = haptics.DrawingPulses,
                overallStrength = activeSettings.Intensity,
                drawingStrength = activeSettings.DrawingStrength,
                drawingInterval = activeSettings.DrawingIntervalMilliseconds,
                drawingOnlyWhenMoving = activeSettings.DrawingOnlyWhenMoving,
                stopCommands = haptics.StopCommands,
                stopDiagnostic = haptics.StopDiagnostic,
                display = new
                {
                    dpi = DeviceDpi,
                    width = Width,
                    height = Height,
                    designScale = AutoScaleDimensions.ToString(),
                    currentScale = CurrentAutoScaleDimensions.ToString()
                }
            };
            File.WriteAllText(Path.Combine(Settings.DataPath, "status.json"), JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    public void Shutdown()
    {
        if (shuttingDown)
            return;
        shuttingDown = true;
        ResetGesture();
        statusTimer.Stop();
        runtimeTimer.Stop();
        previewTimer.Stop();
        pauseShortcut?.Dispose();
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        if (hook != 0)
        {
            Native.UnhookWindowsHookEx(hook);
            hook = 0;
        }

        raw?.Dispose();
        raw = null;
        haptics.Dispose();
        tray.Visible = false;
        tray.Dispose();
        try
        {
            File.WriteAllText(Path.Combine(Settings.DataPath, "status.json"), "{\"running\":false}");
        }
        catch
        {
        }

        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Shutdown();
            holdTimer.Dispose();
            statusTimer.Dispose();
            fallbackTimer.Dispose();
            runtimeTimer.Dispose();
            previewTimer.Dispose();
            appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool ForegroundMatches()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        return pid != 0 && pid == foregroundPid && foregroundProcess != null;
    }

    private void UpdateActiveProfile(bool force = false)
    {
        if (calibrating || shuttingDown)
            return;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        if (!force && pid == foregroundPid && foregroundProcess != null)
            return;
        string? name = null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch
        {
        }

        var profile = name == null || pid == Environment.ProcessId ? null : profiles.Resolve(name);
        string id = profile?.Id ?? "default";
        bool changed = id != activeProfileId;
        foregroundPid = pid;
        foregroundProcess = name;
        if (!force && !changed)
            return;
        // Stop contact feedback before switching. A top-button sequence belongs
        // to its initial press, so opening Windows' pen menu must not cut it short.
        contact.Reset();
        mouseContact = false;
        holdTimer.Stop();
        fallback.Clear();
        fallbackTimer.Stop();
        if (force)
            haptics.Cancel();
        else
            haptics.CancelContact();
        activeProfileId = id;
        activeProfileName = profile?.Name ?? "Default settings";
        activeSettings = profile?.Feedback ?? settings;
        haptics.Configure(activeSettings, preserveShortcut: !force);
        gestures.HoldMilliseconds = activeSettings.HoldMilliseconds;
        gestures.DoubleTapMilliseconds = activeSettings.DoubleTapMilliseconds == 0 ? (int)Native.GetDoubleClickTime() : activeSettings.DoubleTapMilliseconds;
        stroke.MovementThreshold = activeSettings.DrawingMovementThreshold * DeviceDpi / 96d;
        stroke.IdleMilliseconds = activeSettings.DrawingStopMilliseconds;
    }

    private void TryReconnect()
    {
        if (!haptics.Connected && !calibrating && desktopOptions.AutoReconnect)
            Connect(0, false);
    }

    private void SaveProfiles()
    {
        try
        {
            LocalJson.Save(ProfilesPath, profiles);
            featureFault = "";
            UpdateActiveProfile(true);
        }
        catch (Exception e)
        {
            featureFault = e.Message;
            throw;
        }
    }

    private void SaveDesktopOptions()
    {
        desktopOptions.Normalize();
        try
        {
            LocalJson.Save(DesktopOptionsPath, desktopOptions);
            featureFault = "";
            pauseShortcut?.Apply(desktopOptions);
            reconnectSchedule.Reset(Environment.TickCount64);
            UpdateStatus();
        }
        catch (Exception e)
        {
            featureFault = e.Message;
            throw;
        }
    }

    private DiagnosticSnapshot GetDiagnosticSnapshot()
    {
        long age = raw is { Reports: > 0 } ? Math.Max(0, Environment.TickCount64 - raw.LastReportAt) : -1;
        string reconnect = !desktopOptions.AutoReconnect ? "Automatic reconnect is off; touch the blue pad to connect." : haptics.Connected ? "Connected." : $"Automatic retry enabled ({reconnectSchedule.Attempts} attempts). Fresh pen input retries sooner; the blue pad remains available.";
        return new DiagnosticSnapshot(Application.ProductVersion.Split('+')[0], haptics.Connected, paused, sessionLocked || powerSuspended, IsExcluded() || (!calibrating && !ForegroundMatches()), inputFault.Length > 0, activeSettings.Intensity, activeSettings.DrawingFeedback, activeSettings.DrawingStrength, activeProfileName, raw?.Reports ?? 0, age, pressureSeen, haptics.IntensitySupported, haptics.TextureStatus, reconnect, pauseShortcut?.Status ?? "Shortcut is starting.", haptics.ConnectionMethod + ": " + haptics.ConnectionStage, haptics.RoutingDiagnostic, saveFault + featureFault);
    }

    private void Calibrate()
    {
        if (!haptics.Connected || paused)
        {
            MessageBox.Show("Connect the pen on the blue pad and resume feedback before calibrating.", "PenPulse");
            return;
        }

        ResetGesture();
        calibrating = true;
        try
        {
            using var wizard = new CalibrationForm(settings, haptics.AvailableTextures, PreviewCalibration, StopCalibrationPreview);
            if (wizard.ShowDialog(this) == DialogResult.OK)
            {
                settings.CopyFeedbackFrom(wizard.Result);
                configuration.Reload();
                SaveSettings();
            }
        }
        finally
        {
            StopCalibrationPreview();
            calibrating = false;
            UpdateActiveProfile(true);
            UpdateStatus();
        }
    }

    private void PreviewCalibration(Settings draft, Gesture? gesture)
    {
        StopCalibrationPreview();
        if (paused || !haptics.Connected || sessionLocked || powerSuspended)
            return;
        haptics.Configure(draft);
        if (gesture is Gesture action)
            haptics.Play(action);
        else
        {
            previewUntil = Environment.TickCount64 + 800;
            haptics.SetDrawing(true, .6);
            previewTimer.Start();
        }
    }

    private void StopCalibrationPreview()
    {
        previewTimer.Stop();
        haptics.Cancel();
    }

    private static Icon MakeIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var b = new SolidBrush(Color.FromArgb(32, 99, 211));
        g.FillEllipse(b, 1, 1, 30, 30);
        using var p = new Pen(Color.White, 3);
        g.DrawLines(p, new Point[] { new(7, 18), new(12, 18), new(15, 10), new(18, 23), new(21, 16), new(26, 16) });
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }
}

internal sealed class PenPad : Control
{
    public event Action<uint>? Pointer;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Connected
    {
        get; set;
    }

    public PenPad()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(229, 239, 255);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "Pen connection and gesture test pad";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int S(int x) => (int)Math.Round(x * DeviceDpi / 96d);
        using var title = new Font("Segoe UI", 14, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, Connected ? "Connected — try your pen here" : "Touch here with your pen", title, new Rectangle(S(12), S(20), Width - S(24), S(32)), Color.FromArgb(32, 85, 167), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, Connected ? "Draw, tap or hold here while you adjust the settings." : "Use the pen tip, not your finger or mouse.", Font, new Rectangle(S(12), S(58), Width - S(24), S(28)), Color.FromArgb(54, 82, 124), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is Native.WM_POINTERENTER or Native.WM_POINTERDOWN or Native.WM_POINTERUPDATE)
        {
            uint id = (uint)((long)m.WParam & 0xffff);
            if (Native.GetPointerType(id, out var type) && type == 3)
                Pointer?.Invoke(id);
        }

        base.WndProc(ref m);
    }
}
