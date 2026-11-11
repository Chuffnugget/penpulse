using System.Text.Json;

namespace PenPulse;

internal sealed class SavedProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public Settings Feedback { get; set; } = new();

    public override string ToString() => Name;
}

internal sealed class ProfileLibrary
{
    public int FormatVersion { get; set; } = 1;
    public bool AutomaticSwitching { get; set; } = true;
    public List<SavedProfile> Profiles { get; set; } = new();
    public Dictionary<string, string> AppProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        if (FormatVersion != 1)
            throw new InvalidDataException("This profile library needs a newer PenPulse version.");
        Profiles ??= new();
        AppProfiles ??= new();
        if (Profiles.Count > 64 || AppProfiles.Count > 256)
            throw new InvalidDataException("Profile library exceeds its size limit.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Profiles)
        {
            if (p == null || !Guid.TryParseExact(p.Id, "N", out _) || !ids.Add(p.Id))
                throw new InvalidDataException("Invalid or duplicate profile ID.");
            p.Name = ValidateName(p.Name);
            p.Feedback = (p.Feedback ?? new Settings()).CloneFeedback();
        }

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in AppProfiles)
        {
            string name = ProcessName(pair.Key);
            if (!ids.Contains(pair.Value))
                continue;
            if (!mappings.TryAdd(name, pair.Value))
                throw new InvalidDataException("Duplicate app assignment.");
        }

        AppProfiles = mappings;
    }

    public SavedProfile? Resolve(string? processName)
    {
        if (!AutomaticSwitching || string.IsNullOrWhiteSpace(processName))
            return null;
        return AppProfiles.TryGetValue(ProcessName(processName), out string? id) ? Profiles.Find(p => p.Id == id) : null;
    }

    public SavedProfile Add(string name, Settings feedback)
    {
        if (Profiles.Count >= 64)
            throw new InvalidOperationException("The library already contains 64 profiles.");
        name = ValidateName(name);
        if (Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose a different profile name.");
        var profile = new SavedProfile
        {
            Name = name,
            Feedback = feedback.CloneFeedback()
        };
        Profiles.Add(profile);
        return profile;
    }

    public void Remove(string id)
    {
        Profiles.RemoveAll(p => p.Id == id);
        foreach (var app in AppProfiles.Where(p => p.Value == id).Select(p => p.Key).ToArray())
            AppProfiles.Remove(app);
    }

    public static string ValidateName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name.Any(char.IsControl))
            throw new InvalidDataException("Profile names must contain 1–64 printable characters.");
        return name;
    }

    public static string ProcessName(string value)
    {
        value = value.Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        if (value.Length is 0 or > 120 || value.Any(c => char.IsControl(c) || "\\/:*?\"<>|;".Contains(c)))
            throw new InvalidDataException("Enter an executable name, such as OneNote.exe, without its folder path.");
        return value.ToLowerInvariant();
    }
}

internal sealed class ProfileExport
{
    public string Format { get; set; } = "PenPulseProfile";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public Settings Feedback { get; set; } = new();
}

internal static class ProfileTransfer
{
    public const int MaximumBytes = 1024 * 1024;
    public static string Export(SavedProfile profile) => JsonSerializer.Serialize(new ProfileExport { Name = ProfileLibrary.ValidateName(profile.Name), Feedback = profile.Feedback.CloneFeedback() }, LocalJson.Options);
    public static SavedProfile Import(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidDataException("Profile files must be smaller than 1 MB.");
        var file = JsonSerializer.Deserialize<ProfileExport>(json, LocalJson.Options) ?? throw new InvalidDataException("Empty profile.");
        // Require an explicit envelope: arbitrary settings JSON is not a portable profile.
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        if (!doc.RootElement.TryGetProperty("Format", out _) || !doc.RootElement.TryGetProperty("Version", out _) || !doc.RootElement.TryGetProperty("Feedback", out _) || file.Format != "PenPulseProfile" || file.Version != 1 || file.Feedback == null)
            throw new InvalidDataException("This is not a supported PenPulse profile file.");
        return new SavedProfile
        {
            Name = ProfileLibrary.ValidateName(file.Name),
            Feedback = file.Feedback.CloneFeedback()
        };
    }
}
