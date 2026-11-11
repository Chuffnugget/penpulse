using System.Text.Json;

namespace PenPulse;

internal static class LocalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        MaxDepth = 24
    };
    public static T Load<T>(string path, out string error)
        where T : new()
    {
        error = "";
        try
        {
            if (!File.Exists(path))
                return new T();
            if (new FileInfo(path).Length > 2 * 1024 * 1024)
                throw new InvalidDataException("Settings file is too large.");
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty settings file.");
        }
        catch (Exception e)
        {
            error = e.Message;
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        // Preserve the previous file, including an unreadable file the user may need to recover.
        if (File.Exists(path))
            File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }
}
