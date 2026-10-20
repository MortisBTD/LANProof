using System.Text.Json;

namespace LANProof.Runtime;

public static class ApplicationPaths
{
    public static string DataDirectory => ResolveDataDirectory(AppContext.BaseDirectory);
    public static string ThemeFile => Path.Combine(DataDirectory, "appearance.json");

    public static string ResolveDataDirectory(string executableDirectory)
    {
        for (var folder = new DirectoryInfo(executableDirectory); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "LANProof.sln")))
                return Path.Combine(folder.FullName, "data");
        return Path.Combine(Path.GetFullPath(executableDirectory), "data");
    }
}

public sealed class ThemePreferences
{
    public bool DarkMode { get; set; } = true;

    public static ThemePreferences Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ThemePreferences>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public async Task SaveAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
