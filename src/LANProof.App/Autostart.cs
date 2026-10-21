using Microsoft.Win32;

namespace LANProof.App;

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LANProof";

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command && string.Equals(command, Command(), StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Автозапуск настраивается только в Windows.");

        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("Раздел автозапуска недоступен.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        key.SetValue(ValueName, Command());
    }

    private static string Command()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Путь к программе не определён.");
        if (!string.Equals(Path.GetFileNameWithoutExtension(exe), "LANProof", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Для автозапуска запустите установленный LANProof.exe.");
        return $"\"{exe}\" --background";
    }
}
