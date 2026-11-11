using PenPulse;
using System.Runtime.InteropServices;

var failures = new List<string>();
int tests = 0;
void Check(string name, Action test)
{
    tests++;
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception e)
    {
        failures.Add(name + ": " + e.Message);
        Console.WriteLine("FAIL " + name + ": " + e.Message);
    }
}

void Equal<T>(T actual, T expected)
{
    if (!Equals(actual, expected))
        throw new Exception($"Expected {expected}; got {actual}");
}

void Events(List<Gesture> actual, params Gesture[] expected)
{
    Equal(string.Join(",", actual), string.Join(",", expected));
}

(GestureRecognizer r, List<Gesture> e) Make()
{
    var r = new GestureRecognizer();
    var e = new List<Gesture>();
    r.Recognized += e.Add;
    return (r, e);
}

PenSample S(bool contact, double x = 100, double y = 100, bool barrel = false, bool erase = false, bool range = true) => new(contact, barrel, erase, range, x, y);
Check("Tap emits once", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Feed(S(true), 10010);
    r.Feed(S(false), 10080);
    Events(e, Gesture.Press);
});
Check("Hold threshold and one-time firing", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Tick(10549);
    Events(e, Gesture.Press);
    r.Tick(10550);
    r.Tick(12000);
    Events(e, Gesture.Press, Gesture.Hold);
});
Check("Hold right-click promotion is suppressed", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Tick(10600);
    r.Feed(S(false), 10700);
    r.PromotedRightClick(10720);
    Events(e, Gesture.Press, Gesture.Hold);
});
Check("Barrel contact is a right-click, never a hold", () =>
{
    var (r, e) = Make();
    r.Feed(S(true, barrel: true), 10000);
    r.Tick(12000);
    r.PromotedRightClick(12010);
    r.Feed(S(false), 12100);
    Events(e, Gesture.RightClick);
});
Check("Barrel pressed mid-contact", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Feed(S(true, barrel: true), 10100);
    r.Tick(12000);
    Events(e, Gesture.Press, Gesture.RightClick);
});
Check("Movement cancels hold; release is distinct", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Feed(S(true, x: 130), 10100);
    r.Tick(11000);
    r.Feed(S(false, x: 140), 11100);
    Events(e, Gesture.Press, Gesture.Drag, Gesture.Release);
});
Check("Hover never causes a gesture", () =>
{
    var (r, e) = Make();
    r.Feed(S(false), 10000);
    r.Tick(12000);
    Events(e);
});
Check("Eraser has own contact pattern", () =>
{
    var (r, e) = Make();
    r.Feed(S(true, erase: true), 10000);
    r.Tick(11000);
    r.Feed(S(false), 11100);
    Events(e, Gesture.Eraser);
});
Check("Double-tap has distance and time limits", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Feed(S(false), 10080);
    r.Feed(S(true, x: 104), 10300);
    Events(e, Gesture.Press, Gesture.DoubleTap);
});
Check("Distant quick taps are separate presses", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Feed(S(false), 10080);
    r.Feed(S(true, x: 200), 10300);
    Events(e, Gesture.Press, Gesture.Press);
});
Check("Late second tap is a new press", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Feed(S(false), 10080);
    r.Feed(S(true), 11000);
    Events(e, Gesture.Press, Gesture.Press);
});
Check("Out of range cancels pending hold", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Feed(S(false, range: false), 10100);
    r.Tick(12000);
    Equal(r.Contact, false);
    Events(e, Gesture.Press);
});
Check("Pause or session reset clears pending gestures", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 10000);
    r.Reset();
    r.Tick(12000);
    r.Feed(S(true), 12100);
    Events(e, Gesture.Press, Gesture.Press);
});
Check("Repeated promoted right clicks are deduplicated", () =>
{
    var (r, e) = Make();
    r.PromotedRightClick(10000);
    r.PromotedRightClick(10010);
    Events(e, Gesture.RightClick);
});
Check("Physical mouse and touch are excluded", () =>
{
    Equal(PenSignature.IsPen(0), false);
    Equal(PenSignature.IsPen(0xff515780), false);
    Equal(PenSignature.IsPen(0xff515701), true);
    Equal(PenSignature.IsPen(0xffffffff_ff515701), true);
});
Check("Native HID structures have Windows ABI sizes", () =>
{
    Equal(Marshal.SizeOf<Native.ValueCaps>(), 72);
    Equal(Marshal.SizeOf<Native.ButtonCaps>(), 72);
    Equal(Marshal.SizeOf<Native.HidCaps>(), 64);
    Equal(Marshal.SizeOf<Native.RawHeader>(), IntPtr.Size == 8 ? 24 : 16);
});
Check("Drawing requires contact and real motion", () =>
{
    var s = new StrokeMotion();
    s.Feed(S(false), 1000);
    s.Feed(S(false, x: 200), 1010);
    Equal(s.IsMoving(1010), false);
    s.Feed(S(true, x: 200), 1020);
    Equal(s.IsMoving(1020), false);
    s.Feed(S(true, x: 204), 1030);
    Equal(s.IsMoving(1030), true);
});
Check("Drawing ignores stationary jitter but accumulates slow movement", () =>
{
    var s = new StrokeMotion();
    s.Feed(S(true), 1000);
    s.Feed(S(true, x: 101), 1010);
    s.Feed(S(true, x: 99), 1020);
    Equal(s.IsMoving(1020), false);
    s.Feed(S(true, x: 102), 1030);
    Equal(s.IsMoving(1030), true);
});
Check("Drawing expires on stationary or missing input and resumes on motion", () =>
{
    var s = new StrokeMotion();
    s.Feed(S(true), 1000);
    s.Feed(S(true, x: 110), 1010);
    s.Feed(S(true, x: 110), 1149);
    Equal(s.IsMoving(1149), true);
    Equal(s.IsMoving(1150), false);
    s.Feed(S(true, x: 120), 1160);
    Equal(s.IsMoving(1160), true);
});
Check("Pen lift stops drawing immediately", () =>
{
    var s = new StrokeMotion();
    s.Feed(S(true), 1000);
    s.Feed(S(true, x: 110), 1010);
    s.Feed(S(false, x: 110), 1011);
    Equal(s.IsMoving(1011), false);
    s.Feed(S(true, x: 110), 1012);
    Equal(s.IsMoving(1012), false);
});
Check("Proximity loss and pause reset drawing", () =>
{
    var s = new StrokeMotion();
    s.Feed(S(true), 1000);
    s.Feed(S(true, x: 110), 1010);
    s.Feed(S(true, x: 110, range: false), 1011);
    Equal(s.IsMoving(1011), false);
    s.Feed(S(true), 1020);
    s.Feed(S(true, x: 110), 1030);
    s.Reset();
    Equal(s.IsMoving(1030), false);
});
Check("Movement feedback includes barrel drags and erasing", () =>
{
    var s = new StrokeMotion();
    s.Feed(S(true, barrel: true), 1000);
    s.Feed(S(true, x: 110, barrel: true), 1010);
    Equal(s.IsMoving(1010), true);
    s.Reset();
    s.Feed(S(true, erase: true), 1020);
    s.Feed(S(true, x: 110, erase: true), 1030);
    Equal(s.IsMoving(1030), true);
});
Check("Fast motion reports cannot flood drawing pulses", () =>
{
    var c = new DrawingPulseClock();
    int count = 0;
    for (int now = 1000; now < 1250; now++)
    {
        c.Request(true, now);
        if (c.TryTakePulse(now))
            count++;
    }

    Equal(count, 3);
});
Check("Drawing pulse requests expire when input disappears", () =>
{
    var c = new DrawingPulseClock();
    c.Request(true, 1000);
    Equal(c.TryTakePulse(1000), true);
    Equal(c.TryTakePulse(1200), false);
    Equal(c.IsRequested(1200), false);
});
Check("Lift cancels future drawing pulses and a fresh stroke restarts", () =>
{
    var c = new DrawingPulseClock();
    c.Request(true, 1000);
    Equal(c.TryTakePulse(1000), true);
    c.Request(false, 1001);
    Equal(c.TryTakePulse(1100), false);
    c.Request(true, 1101);
    Equal(c.TryTakePulse(1101), true);
});
Check("Delayed drawing feedback never replays a burst", () =>
{
    var c = new DrawingPulseClock();
    c.Request(true, 1000);
    Equal(c.TryTakePulse(1000), true);
    c.Request(true, 1190);
    Equal(c.TryTakePulse(1190), true);
    Equal(c.TryTakePulse(1190), false);
    Equal(c.TryTakePulse(1191), false);
    Equal(c.TryTakePulse(1275), true);
});
Check("Old saved preferences retain strength and switches", () =>
{
    var s = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Intensity\":1,\"DoubleTapFeedback\":false,\"EraserFeedback\":false,\"DrawingFeedback\":true}")!;
    s.Normalize();
    Equal(s.Intensity, 1d);
    Equal(s.DoubleTap.Enabled, false);
    Equal(s.Eraser.Enabled, false);
    Equal(s.DrawingStrength, 1d);
    Equal(s.DrawingIntervalMilliseconds, 85);
    Equal(s.SchemaVersion, 1);
});
Check("Custom effects survive a settings round-trip independently", () =>
{
    var s = new Settings();
    s.Normalize();
    s.Tap.PulseCount = 5;
    s.Tap.Strength = .2;
    s.Hold.Enabled = false;
    s.DrawingStrength = 1;
    var copy = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(s))!;
    copy.Normalize();
    Equal(copy.Tap.PulseCount, 5);
    Equal(copy.Hold.Enabled, false);
    Equal(copy.RightClick.PulseCount, 3);
    Equal(copy.DrawingStrength, 1d);
    Equal(copy.Tap.Strength, .2);
});
Check("Default patterns use full strength with distinct timing", () =>
{
    var s = new Settings();
    s.Normalize();
    Equal(s.Intensity, 1d);
    Equal(s.DrawingStrength, 1d);
    foreach (var gesture in Enum.GetValues<Gesture>())
        Equal(s.For(gesture).Strength, 1d);
    var h = s.Hold.Pattern();
    Equal(h.Length, 2);
    Equal(h[1].Offset, 150);
    Equal(h[0].Strength, 1d);
    var r = s.RightClick.Pattern();
    Equal(r[2].Offset, 170);
    Equal(r[2].Strength, 1d);
    var e = s.Eraser.Pattern();
    Equal(e[0].Strength, .625);
    Equal(e[1].Strength, 1d);
});
Check("Invalid configuration is bounded before playback", () =>
{
    var s = new Settings
    {
        Intensity = double.NaN,
        DrawingStrength = 2,
        DrawingIntervalMilliseconds = 0,
        DrawingStopMilliseconds = 9999,
        DrawingMovementThreshold = double.PositiveInfinity
    };
    s.Hold.PulseCount = 100000;
    s.Hold.GapMilliseconds = -1;
    s.Hold.Strength = -1;
    s.Normalize();
    Equal(s.Intensity, 1d);
    Equal(s.DrawingStrength, 1d);
    Equal(s.DrawingIntervalMilliseconds, 50);
    Equal(s.DrawingStopMilliseconds, 300);
    Equal(s.DrawingMovementThreshold, 2d);
    Equal(s.Hold.Pattern().Length, 6);
    Equal(s.Hold.GapMilliseconds, 50);
    Equal(s.Hold.Strength, 0d);
});
Check("Presets preserve app exclusions and custom patterns", () =>
{
    var s = new Settings
    {
        ExcludedApps = "OneNote;Photoshop"
    };
    s.Normalize();
    s.Tap.PulseCount = 3;
    s.ApplyPreset("Strong");
    Equal(s.DrawingStrength, 1d);
    Equal(s.DrawingIntervalMilliseconds, 60);
    Equal(s.ExcludedApps, "OneNote;Photoshop");
    Equal(s.Tap.PulseCount, 3);
});
Check("Configured drawing rate takes effect without an input flood", () =>
{
    var c = new DrawingPulseClock
    {
        IntervalMilliseconds = 50
    };
    int count = 0;
    for (int now = 1000; now < 1250; now++)
    {
        c.Request(true, now);
        if (c.TryTakePulse(now))
            count++;
    }

    Equal(count, 5);
    c.IntervalMilliseconds = 1;
    Equal(c.IntervalMilliseconds, 50);
    c.IntervalMilliseconds = 5000;
    Equal(c.IntervalMilliseconds, 500);
});
Check("Stationary feedback mode still expires and stops on lift", () =>
{
    var s = new StrokeMotion();
    s.Feed(S(true), 1000);
    Equal(s.IsMoving(1100), false);
    Equal(s.IsContactRecent(1100), true);
    Equal(s.IsContactRecent(1400), false);
    s.Feed(S(true), 1410);
    Equal(s.IsContactRecent(1410), true);
    s.Feed(S(false), 1411);
    Equal(s.IsContactRecent(1411), false);
});
Check("Eraser-only contact produces the eraser effect", () =>
{
    var (r, e) = Make();
    var p = PenSample.FromSwitches(false, true, true, false, true, 100, 100);
    Equal(p.Contact, true);
    Equal(p.Eraser, true);
    r.Feed(p, 1000);
    r.Tick(2000);
    Events(e, Gesture.Eraser);
});
Check("An inverted hovering pen never counts as touching", () =>
{
    var (r, e) = Make();
    var p = PenSample.FromSwitches(false, false, true, false, true, 100, 100);
    Equal(p.Contact, false);
    r.Feed(p, 1000);
    r.Tick(2000);
    Events(e);
});
Check("Eraser lift stops movement even while inverted remains set", () =>
{
    var motion = new StrokeMotion();
    motion.Feed(PenSample.FromSwitches(false, true, true, false, true, 100, 100), 1000);
    motion.Feed(PenSample.FromSwitches(false, true, true, false, true, 120, 100), 1010);
    Equal(motion.IsMoving(1010), true);
    motion.Feed(PenSample.FromSwitches(false, false, true, false, true, 120, 100), 1011);
    Equal(motion.IsMoving(1011), false);
    Equal(motion.IsContactRecent(1011), false);
});
Check("Eraser contact does not require an invert flag", () =>
{
    var (r, e) = Make();
    r.Feed(PenSample.FromSwitches(false, true, false, false, true, 100, 100), 1000);
    Events(e, Gesture.Eraser);
});
Check("Normal tip still maps to a single tap", () =>
{
    var (r, e) = Make();
    r.Feed(PenSample.FromSwitches(true, false, false, false, true, 100, 100), 1000);
    r.Feed(PenSample.FromSwitches(false, false, false, false, true, 100, 100), 1010);
    Events(e, Gesture.Press);
});
Check("Top button maps Windows click, double-click and hold chords", () =>
{
    foreach (var (key, gesture) in new[]
    {
        (0x83, Gesture.TopClick),
        (0x82, Gesture.TopDoubleClick),
        (0x81, Gesture.TopHold)
    }

    )
    {
        var r = new PenShortcutRecognizer();
        r.Feed(1, 0x5b, 0, 0, 1000);
        Equal(r.Feed(1, (ushort)key, 0, 0, 1001), (Gesture?)gesture);
    }
});
Check("Top button ignores ordinary keys and bare function keys", () =>
{
    var r = new PenShortcutRecognizer();
    Equal(r.Feed(1, 0x83, 0, 0, 1000), null);
    r.Feed(1, 0x5b, 0, 0, 1001);
    Equal(r.Feed(1, 0x41, 0, 0, 1002), null);
    Equal(r.Feed(1, 0x70, 0, 0, 1003), null);
});
Check("Pen dock and undock chords do not buzz", () =>
{
    foreach (ushort key in new ushort[]
    {
        0x83,
        0x82
    }

    )
    {
        var r = new PenShortcutRecognizer();
        r.Feed(1, 0x5b, 0, 0, 1000);
        r.Feed(1, 0x11, 0, 0, 1001);
        Equal(r.Feed(1, key, 0, 0, 1002), null);
    }
});
Check("Top button auto-repeat and release do not duplicate a pattern", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5b, 0, 0, 1000);
    Equal(r.Feed(1, 0x83, 0, 0, 1001), (Gesture?)Gesture.TopClick);
    Equal(r.Feed(1, 0x83, 0, 0, 1100), null);
    Equal(r.Feed(1, 0x83, 1, 0, 1101), null);
    Equal(r.Feed(1, 0x83, 0, 0, 1200), (Gesture?)Gesture.TopClick);
});
Check("Keyboard modifiers cannot combine across devices", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5b, 0, 0, 1000);
    Equal(r.Feed(2, 0x83, 0, 0, 1001), null);
    Equal(r.Feed(0, 0x5b, 0, 0, 1002), null);
    Equal(r.Feed(0, 0x83, 0, 0, 1003), null);
});
Check("Device removal, session reset and stale modifiers cancel shortcuts", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5b, 0, 0, 1000);
    r.DeviceRemoved(1);
    Equal(r.Feed(1, 0x83, 0, 0, 1001), null);
    r.Feed(2, 0x5b, 0, 0, 1000);
    r.Reset();
    Equal(r.Feed(2, 0x83, 0, 0, 1001), null);
    r.Feed(3, 0x5b, 0, 0, 1000);
    Equal(r.Feed(3, 0x83, 0, 0, 4000), null);
});
Check("Right Windows key works and a released Windows key does not", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5c, 2, 0, 1000);
    Equal(r.Feed(1, 0x82, 0, 0, 1001), (Gesture?)Gesture.TopDoubleClick);
    r.Feed(1, 0x5c, 3, 0, 1002);
    Equal(r.Feed(1, 0x81, 0, 0, 1003), null);
});
Check("Both Control keys are tracked independently when docking", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5b, 0, 0, 1000);
    r.Feed(1, 0x11, 0, 0, 1001);
    r.Feed(1, 0x11, 2, 0, 1002);
    r.Feed(1, 0x11, 1, 0, 1003);
    Equal(r.Feed(1, 0x83, 0, 0, 1004), null);
});
Check("Shortcut settings migrate without changing eraser contact or master strength", () =>
{
    var s = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"SchemaVersion\":1,\"Intensity\":1,\"Eraser\":{\"Strength\":0.4,\"PulseCount\":5},\"TopClick\":null}")!;
    s.Normalize();
    Equal(s.Intensity, 1d);
    Equal(s.Eraser.Strength, .4);
    Equal(s.Eraser.PulseCount, 5);
    Equal(s.TopClick.Strength, 1d);
    Equal(s.TopClick.PulseCount, 1);
    Equal(s.TopDoubleClick.PulseCount, 2);
    Equal(s.TopHold.GapMilliseconds, 150);
    s.TopClick.Enabled = false;
    Equal(s.Eraser.Enabled, true);
});
Check("Raw keyboard packets have the Windows ABI size", () => Equal(Marshal.SizeOf<Native.RawKeyboard>(), 16));
Check("Observed Slim Pen Win-down Win-up F20-up sequence emits once", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5b, 2, 91, 1000);
    r.Feed(1, 0x5b, 3, 91, 1000);
    Equal(r.Feed(1, 0x83, 1, 107, 1000, true), (Gesture?)Gesture.TopClick);
    Equal(r.Feed(1, 0x83, 1, 107, 1001, true), null);
    r.Feed(1, 0x5b, 2, 91, 2000);
    r.Feed(1, 0x5b, 3, 91, 2000);
    Equal(r.Feed(1, 0x83, 1, 107, 2000, true), (Gesture?)Gesture.TopClick);
});
Check("Pen release fallback covers double-click and hold too", () =>
{
    foreach (var (key, g) in new[]
    {
        (0x82, Gesture.TopDoubleClick),
        (0x81, Gesture.TopHold)
    }

    )
    {
        var r = new PenShortcutRecognizer();
        r.Feed(1, 0x5b, 2, 91, 1000);
        r.Feed(1, 0x5b, 3, 91, 1001);
        Equal(r.Feed(1, (ushort)key, 1, 0, 1002, true), (Gesture?)g);
    }
});
Check("Ordinary keyboard orphan releases remain ignored", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5b, 2, 91, 1000);
    r.Feed(1, 0x5b, 3, 91, 1001);
    Equal(r.Feed(1, 0x83, 1, 107, 1002, false), null);
});
Check("A full shortcut press and release cannot play twice", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x5b, 2, 91, 1000);
    Equal(r.Feed(1, 0x83, 0, 107, 1001, true), (Gesture?)Gesture.TopClick);
    r.Feed(1, 0x5b, 3, 91, 1002);
    Equal(r.Feed(1, 0x83, 1, 107, 1003, true), null);
});
Check("Orphan pen releases require a fresh same-device Windows event", () =>
{
    var r = new PenShortcutRecognizer();
    Equal(r.Feed(1, 0x83, 1, 107, 1000, true), null);
    r.Feed(2, 0x5b, 2, 91, 1000);
    r.Feed(2, 0x5b, 3, 91, 1001);
    Equal(r.Feed(1, 0x83, 1, 107, 1002, true), null);
    Equal(r.Feed(2, 0x83, 1, 107, 1251, true), null);
});
Check("Dock release is rejected even if Control released first", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x11, 0, 29, 1000);
    r.Feed(1, 0x5b, 2, 91, 1001);
    r.Feed(1, 0x11, 1, 29, 1002);
    r.Feed(1, 0x5b, 3, 91, 1003);
    Equal(r.Feed(1, 0x83, 1, 107, 1004, true), null);
});
Check("Recently released docking modifier cannot become a button click", () =>
{
    var r = new PenShortcutRecognizer();
    r.Feed(1, 0x11, 0, 29, 1000);
    r.Feed(1, 0x11, 1, 29, 1001);
    r.Feed(1, 0x5b, 2, 91, 1002);
    r.Feed(1, 0x5b, 3, 91, 1003);
    Equal(r.Feed(1, 0x82, 1, 107, 1004, true), null);
});
Check("Pen release fallback excludes extra Alt and Shift", () =>
{
    foreach (ushort modifier in new ushort[]
    {
        0x10,
        0x12
    }

    )
    {
        var r = new PenShortcutRecognizer();
        r.Feed(1, 0x5b, 2, 91, 1000);
        r.Feed(1, modifier, 0, 0, 1001);
        r.Feed(1, modifier, 1, 0, 1002);
        r.Feed(1, 0x5b, 3, 91, 1003);
        Equal(r.Feed(1, 0x83, 1, 107, 1004, true), null);
    }
});
Check("Pen identity matches Bluetooth Slim Pen keyboard, not generic keyboards", () =>
{
    string p = @"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02045e_PID&0c0f_REV&0596_000000000000&Col01#example";
    Equal(PenKeyboardIdentity.SupportsReleaseFallback(p), true);
    Equal(PenKeyboardIdentity.SupportsReleaseFallback(p.ToUpperInvariant()), true);
    Equal(PenKeyboardIdentity.SupportsReleaseFallback(p.Replace("0c0f", "ffff")), false);
    Equal(PenKeyboardIdentity.SupportsReleaseFallback(p.Replace("Col01", "Col02")), false);
    Equal(PenKeyboardIdentity.SupportsReleaseFallback(@"\\?\HID#VID_045E&PID_0C0F&Col01#example"), false);
});
Check("Three taps do not reuse the second tap", () =>
{
    var (r, e) = Make();
    foreach (long t in new long[]
    {
        1000,
        1200,
        1400
    }

    )
    {
        r.Feed(S(true), t);
        r.Feed(S(false), t + 50);
    }

    Events(e, Gesture.Press, Gesture.DoubleTap, Gesture.Press);
});
Check("Four taps form two separate pairs", () =>
{
    var (r, e) = Make();
    foreach (long t in new long[]
    {
        1000,
        1200,
        1400,
        1600
    }

    )
    {
        r.Feed(S(true), t);
        r.Feed(S(false), t + 50);
    }

    Events(e, Gesture.Press, Gesture.DoubleTap, Gesture.Press, Gesture.DoubleTap);
});
Check("Hold on the second tap does not prime a third double-tap", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 1000);
    r.Feed(S(false), 1050);
    r.Feed(S(true), 1200);
    r.Tick(1750);
    r.Feed(S(false), 1800);
    r.Feed(S(true), 1900);
    Events(e, Gesture.Press, Gesture.DoubleTap, Gesture.Hold, Gesture.Press);
});
Check("Drag on the second tap does not prime a third double-tap", () =>
{
    var (r, e) = Make();
    r.Feed(S(true), 1000);
    r.Feed(S(false), 1050);
    r.Feed(S(true), 1200);
    r.Feed(S(true, x: 140), 1250);
    r.Feed(S(false, x: 140), 1300);
    r.Feed(S(true, x: 140), 1400);
    Events(e, Gesture.Press, Gesture.DoubleTap, Gesture.Drag, Gesture.Release, Gesture.Press);
});
Check("Raw contact expiry suppresses a phantom hold", () =>
{
    var c = new PenContactState(() => true);
    var seen = new List<Gesture>();
    int expired = 0;
    c.Gestures.Recognized += seen.Add;
    c.Expired += () => expired++;
    c.Feed(S(true), 1000, true);
    c.Tick(1550);
    Equal(c.Gestures.Contact, false);
    Equal(expired, 1);
    Events(seen, Gesture.Press);
});
Check("A fresh raw sample after silence starts a new contact", () =>
{
    var c = new PenContactState(() => true);
    var seen = new List<Gesture>();
    c.Gestures.Recognized += seen.Add;
    c.Feed(S(true), 1000, true);
    c.Feed(S(true), 1600, true);
    c.Tick(1601);
    Events(seen, Gesture.Press, Gesture.Press);
});
Check("Fresh stationary raw reports preserve a real hold", () =>
{
    var c = new PenContactState(() => false);
    var seen = new List<Gesture>();
    c.Gestures.Recognized += seen.Add;
    c.Feed(S(true), 1000, true);
    c.Feed(S(true), 1300, true);
    c.Feed(S(true), 1500, true);
    c.Tick(1550);
    Events(seen, Gesture.Press, Gesture.Hold);
});
Check("Stationary mouse fallback survives without move reports", () =>
{
    bool down = true;
    var c = new PenContactState(() => down);
    var seen = new List<Gesture>();
    c.Gestures.Recognized += seen.Add;
    c.Feed(S(true), 1000, false);
    c.Tick(1550);
    c.Tick(2500);
    Events(seen, Gesture.Press, Gesture.Hold);
    Equal(c.Stroke.IsContactRecent(2500), true);
    down = false;
    c.Tick(2525);
    Equal(c.Gestures.Contact, false);
    Equal(c.Stroke.IsContactRecent(2525), false);
});
Check("Missing mouse-fallback up cannot produce a later hold", () =>
{
    bool down = true;
    var c = new PenContactState(() => down);
    var seen = new List<Gesture>();
    c.Gestures.Recognized += seen.Add;
    c.Feed(S(true), 1000, false);
    down = false;
    c.Tick(1550);
    Events(seen, Gesture.Press);
    Equal(c.Gestures.Contact, false);
});
Check("Queued fallback release still completes a drag", () =>
{
    bool down = true;
    var c = new PenContactState(() => down);
    var seen = new List<Gesture>();
    c.Gestures.Recognized += seen.Add;
    c.Feed(S(true), 1000, false);
    c.Feed(S(true, x: 140), 1050, false);
    down = false;
    c.Feed(S(false, x: 140), 1100, false);
    Events(seen, Gesture.Press, Gesture.Drag, Gesture.Release);
});
Check("Explicit fallback down recovers a stale contact", () =>
{
    var c = new PenContactState(() => true);
    var seen = new List<Gesture>();
    c.Gestures.Recognized += seen.Add;
    c.Feed(S(true), 1000, false);
    c.Feed(S(true), 1200, false, newContact: true);
    Events(seen, Gesture.Press, Gesture.Press);
});
Check("Contact reset clears its hold and drawing state", () =>
{
    var c = new PenContactState(() => true);
    var seen = new List<Gesture>();
    c.Gestures.Recognized += seen.Add;
    c.Feed(S(true), 1000, false);
    c.Reset();
    c.Tick(2000);
    Equal(c.Stroke.IsContactRecent(2000), false);
    Events(seen, Gesture.Press);
});
Haptics Playback(Action<Settings>? configure = null)
{
    PenPulse.Testing.Hardware.Reset();
    Environment.TickCount64 = 1000;
    var s = new Settings();
    s.Normalize();
    configure?.Invoke(s);
    var h = new Haptics();
    h.Configure(s);
    Equal(h.Connect(1), true);
    return h;
}

void PulseTick(long now)
{
    Environment.TickCount64 = now;
    System.Windows.Forms.Timer.Instances.Single().Fire();
}

string SendTimes() => string.Join(",", PenPulse.Testing.Hardware.Sends.Select(s => s.Time));
Check("Real playback scheduler preserves spacing after a delayed timer", () =>
{
    using var h = Playback(s =>
    {
        s.RightClick.PulseCount = 3;
        s.RightClick.GapMilliseconds = 350;
    });
    h.Play(Gesture.RightClick);
    PulseTick(2000);
    PulseTick(2015);
    Equal(SendTimes(), "1000,2000");
    PulseTick(2349);
    Equal(SendTimes(), "1000,2000");
    PulseTick(2350);
    Equal(SendTimes(), "1000,2000,2350");
});
Check("Ordinary top-button double-click retains 350 ms spacing", () =>
{
    using var h = Playback();
    h.Play(Gesture.TopDoubleClick);
    PulseTick(1349);
    Equal(SendTimes(), "1000");
    PulseTick(1350);
    Equal(SendTimes(), "1000,1350");
});
Check("Replacing an effect discards its remaining pulses", () =>
{
    using var h = Playback();
    h.Play(Gesture.TopDoubleClick);
    Environment.TickCount64 = 1100;
    h.Play(Gesture.Press);
    PulseTick(1500);
    Equal(SendTimes(), "1000,1100");
});
Check("Exclusion blocks the very first pulse", () =>
{
    using var h = Playback();
    h.PlaybackAllowed = () => false;
    h.Play(Gesture.TopDoubleClick);
    Equal(SendTimes(), "");
});
Check("Entering an excluded app discards queued pulses permanently", () =>
{
    using var h = Playback();
    bool allowed = true;
    h.PlaybackAllowed = () => allowed;
    h.Play(Gesture.TopDoubleClick);
    allowed = false;
    PulseTick(1200);
    allowed = true;
    PulseTick(1350);
    PulseTick(2000);
    Equal(SendTimes(), "1000");
    Environment.TickCount64 = 2100;
    h.Play(Gesture.Press);
    Equal(SendTimes(), "1000,2100");
});
Check("Exclusion stops a pulse that is still playing", () =>
{
    using var h = Playback();
    bool allowed = true;
    h.PlaybackAllowed = () => allowed;
    h.Play(Gesture.Press);
    allowed = false;
    PulseTick(1005);
    Equal(PenPulse.Testing.Hardware.Stops, 1);
});
Check("Drawing also respects live application exclusion", () =>
{
    using var h = Playback();
    bool allowed = true;
    h.PlaybackAllowed = () => allowed;
    h.SetDrawing(true);
    Equal(SendTimes(), "1000");
    allowed = false;
    Environment.TickCount64 = 1100;
    h.SetDrawing(true);
    PulseTick(1100);
    Equal(SendTimes(), "1000");
    Equal(h.DrawingActive, false);
});
Check("Contact proximity loss preserves an off-screen shortcut sequence", () =>
{
    using var h = Playback();
    h.Play(Gesture.TopDoubleClick);
    PulseTick(1035);
    h.CancelContact();
    PulseTick(1350);
    Equal(SendTimes(), "1000,1350");
});
Check("Contact expiry cancels a pending contact effect", () =>
{
    using var h = Playback(s =>
    {
        s.RightClick.PulseCount = 3;
        s.RightClick.GapMilliseconds = 350;
    });
    h.Play(Gesture.RightClick);
    h.CancelContact();
    PulseTick(2000);
    Equal(SendTimes(), "1000");
});
Check("Pause-style cancellation discards even top-button sequences", () =>
{
    using var h = Playback();
    h.Play(Gesture.TopDoubleClick);
    h.Cancel();
    PulseTick(2000);
    Equal(SendTimes(), "1000");
});
Check("Fresh profiles use verified spacing without overwriting saved preferences", () =>
{
    var s = new Settings();
    s.Normalize();
    Equal(s.TopDoubleClick.GapMilliseconds, 350);
    var old = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"SchemaVersion\":1,\"TopDoubleClick\":{\"GapMilliseconds\":275}}")!;
    old.Normalize();
    Equal(old.TopDoubleClick.GapMilliseconds, 275);
});
FeatureTests.Run(Check);
Console.WriteLine($"\n{tests - failures.Count}/{tests} checks passed.");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;
