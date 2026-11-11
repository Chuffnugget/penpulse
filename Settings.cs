using System.Text.Json;

namespace PenPulse;

internal sealed class Settings
{
    public int SchemaVersion
    {
        get; set;
    }
    public double Intensity { get; set; } = 1;
    public int HoldMilliseconds { get; set; } = 550;
    public int DoubleTapMilliseconds
    {
        get; set;
    } // Zero follows the Windows setting.
    public double DrawingStrength { get; set; } = 1;
    public int DrawingIntervalMilliseconds { get; set; } = 85;
    public int DrawingStopMilliseconds { get; set; } = 140;
    public double DrawingMovementThreshold { get; set; } = 2;
    public bool DrawingOnlyWhenMoving { get; set; } = true;
    public DrawingTexture DrawingTexture { get; set; } = DrawingTexture.Pulses;
    public bool PressureFeedback
    {
        get; set;
    }
    public double PressureSensitivity { get; set; } = 1;
    public double MinimumPressureStrength { get; set; } = .15;
    public bool DragFeedback { get; set; } = false;
    public bool DrawingFeedback { get; set; } = true;
    public bool DoubleTapFeedback { get; set; } = true;
    public bool EraserFeedback { get; set; } = true;
    public string ExcludedApps { get; set; } = "";
    public EffectSettings Tap { get; set; } = DefaultEffect(Gesture.Press);
    public EffectSettings Hold { get; set; } = DefaultEffect(Gesture.Hold);
    public EffectSettings RightClick { get; set; } = DefaultEffect(Gesture.RightClick);
    public EffectSettings DoubleTap { get; set; } = DefaultEffect(Gesture.DoubleTap);
    public EffectSettings Eraser { get; set; } = DefaultEffect(Gesture.Eraser);
    public EffectSettings DragStart { get; set; } = DefaultEffect(Gesture.Drag);
    public EffectSettings DragEnd { get; set; } = DefaultEffect(Gesture.Release);
    public EffectSettings TopClick { get; set; } = DefaultEffect(Gesture.TopClick);
    public EffectSettings TopDoubleClick { get; set; } = DefaultEffect(Gesture.TopDoubleClick);
    public EffectSettings TopHold { get; set; } = DefaultEffect(Gesture.TopHold);
    public static string DataPath => Path.Combine(AppContext.BaseDirectory, "data");
    private static string FilePath => Path.Combine(DataPath, "settings.json");

    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            s.Normalize();
            return s;
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(DataPath);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }

    public EffectSettings For(Gesture gesture) => gesture switch
    {
        Gesture.Hold => Hold,
        Gesture.RightClick => RightClick,
        Gesture.DoubleTap => DoubleTap,
        Gesture.Eraser => Eraser,
        Gesture.Drag => DragStart,
        Gesture.Release => DragEnd,
        Gesture.TopClick => TopClick,
        Gesture.TopDoubleClick => TopDoubleClick,
        Gesture.TopHold => TopHold,
        _ => Tap
    };
    public static string ActionName(Gesture gesture) => gesture switch
    {
        Gesture.Press => "Tap",
        Gesture.Hold => "Long press",
        Gesture.RightClick => "Right-click",
        Gesture.DoubleTap => "Double-tap",
        Gesture.Eraser => "Eraser",
        Gesture.Drag => "Start dragging",
        Gesture.TopClick => "Top button click",
        Gesture.TopDoubleClick => "Top button double-click",
        Gesture.TopHold => "Top button hold",
        _ => "Finish dragging"
    };
    private static EffectSettings DefaultEffect(Gesture gesture) => gesture switch
    {
        Gesture.Hold => new()
        {
            Strength = 1,
            PulseCount = 2,
            GapMilliseconds = 150
        },
        Gesture.RightClick => new()
        {
            Strength = 1,
            PulseCount = 3,
            GapMilliseconds = 85
        },
        Gesture.DoubleTap => new()
        {
            Strength = 1,
            PulseCount = 2,
            GapMilliseconds = 65
        },
        Gesture.Eraser => new()
        {
            Strength = 1,
            PulseCount = 4,
            GapMilliseconds = 110,
            AlternatePulses = true
        },
        Gesture.Drag => new()
        {
            Enabled = false,
            Strength = 1
        },
        Gesture.TopClick => new()
        {
            Strength = 1,
            PulseCount = 1
        },
        Gesture.TopDoubleClick => new()
        {
            Strength = 1,
            PulseCount = 2,
            GapMilliseconds = 350
        },
        Gesture.TopHold => new()
        {
            Strength = 1,
            PulseCount = 3,
            GapMilliseconds = 150
        },
        Gesture.Release => new()
        {
            Enabled = false,
            Strength = 1
        },
        _ => new()
        {
            Strength = 1
        }
    };
    public void Normalize()
    {
        Tap ??= DefaultEffect(Gesture.Press);
        Hold ??= DefaultEffect(Gesture.Hold);
        RightClick ??= DefaultEffect(Gesture.RightClick);
        DoubleTap ??= DefaultEffect(Gesture.DoubleTap);
        Eraser ??= DefaultEffect(Gesture.Eraser);
        DragStart ??= DefaultEffect(Gesture.Drag);
        DragEnd ??= DefaultEffect(Gesture.Release);
        TopClick ??= DefaultEffect(Gesture.TopClick);
        TopDoubleClick ??= DefaultEffect(Gesture.TopDoubleClick);
        TopHold ??= DefaultEffect(Gesture.TopHold);
        if (SchemaVersion < 1)
        {
            DoubleTap.Enabled = DoubleTapFeedback;
            Eraser.Enabled = EraserFeedback;
            DragStart.Enabled = DragEnd.Enabled = DragFeedback;
            SchemaVersion = 1;
        }

        Intensity = double.IsFinite(Intensity) ? Math.Clamp(Intensity, 0, 1) : 1;
        HoldMilliseconds = Math.Clamp(HoldMilliseconds, 250, 1500);
        if (DoubleTapMilliseconds != 0)
            DoubleTapMilliseconds = Math.Clamp(DoubleTapMilliseconds, 150, 1000);
        DrawingStrength = double.IsFinite(DrawingStrength) ? Math.Clamp(DrawingStrength, 0, 1) : 1;
        DrawingIntervalMilliseconds = Math.Clamp(DrawingIntervalMilliseconds, 50, 500);
        DrawingStopMilliseconds = Math.Clamp(DrawingStopMilliseconds, 50, 300);
        DrawingMovementThreshold = double.IsFinite(DrawingMovementThreshold) ? Math.Clamp(DrawingMovementThreshold, 1, 10) : 2;
        if (!Enum.IsDefined(DrawingTexture))
            DrawingTexture = DrawingTexture.Pulses;
        PressureSensitivity = double.IsFinite(PressureSensitivity) ? Math.Clamp(PressureSensitivity, .25, 3) : 1;
        MinimumPressureStrength = double.IsFinite(MinimumPressureStrength) ? Math.Clamp(MinimumPressureStrength, 0, 1) : .15;
        ExcludedApps ??= "";
        foreach (var g in Enum.GetValues<Gesture>())
            For(g).Normalize();
        DoubleTapFeedback = DoubleTap.Enabled;
        EraserFeedback = Eraser.Enabled;
        DragFeedback = DragStart.Enabled || DragEnd.Enabled;
    }

    public void ApplyPreset(string name)
    {
        (Intensity, DrawingStrength, DrawingIntervalMilliseconds) = name switch
        {
            "Soft" => (.6, .4, 120),
            "Strong" => (1d, 1d, 60),
            _ => (.8, .6, 85)
        };
    }

    public Settings CloneFeedback()
    {
        Normalize();
        var clone = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))!;
        clone.Normalize();
        clone.ExcludedApps = "";
        return clone;
    }

    public void CopyFeedbackFrom(Settings source)
    {
        var copy = source.CloneFeedback();
        // Copy only instance feedback properties; exclusions stay local to the PC.
        foreach (var property in typeof(Settings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (property.CanRead && property.CanWrite && property.Name != nameof(ExcludedApps))
                property.SetValue(this, property.GetValue(copy));
    }
}

internal enum DrawingTexture
{
    Pulses,
    Ink,
    Pencil,
    Brush,
    Marker,
    Eraser
}
