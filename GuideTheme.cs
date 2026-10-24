using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

internal static class GuideTheme
{
    private sealed class Settings
    {
        public int Mode { get; set; } = 0;
        public string CustomAccent { get; set; } = "#6ABF40";
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GamesForWindowsLIVE2026", "guide-theme.json");

    private static Settings settings = Load();
    private static string lastSystemAccent = ReadWindowsAccent();
    private static string currentAccent = CalculateAccent();

    // 0 = classic, 1 = Windows accent, 2 = random, 3 = custom.
    public static int Mode => settings.Mode;
    public static string CustomAccent => settings.CustomAccent;
    public static string CurrentAccent => currentAccent;

    public static string ResolveColor(string input)
    {
        if (settings.Mode == 0) return input;
        // Only replace the app's green selection/highlight colors. Neutral surfaces
        // and the classic gold Guide chrome remain unchanged.
        if (input.Equals("#5DAA37", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("#8EDB63", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("#8FD76C", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("#72BD4D", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("#398C28", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("#2D7B20", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("#4C842D", StringComparison.OrdinalIgnoreCase))
            return currentAccent;
        return input;
    }

    public static void SetMode(int mode, string customAccent)
    {
        settings.Mode = Math.Clamp(mode, 0, 3);
        if (!string.IsNullOrWhiteSpace(customAccent) &&
            System.Text.RegularExpressions.Regex.IsMatch(customAccent, "^#([0-9a-fA-F]{6})$"))
            settings.CustomAccent = customAccent.ToUpperInvariant();
        if (settings.Mode == 2)
            currentAccent = RandomAccent();
        else
            currentAccent = CalculateAccent();
        Save();
    }

    public static bool RefreshAccent()
    {
        if (settings.Mode != 1) return false;
        string next = ReadWindowsAccent();
        if (next.Equals(lastSystemAccent, StringComparison.OrdinalIgnoreCase) &&
            next.Equals(currentAccent, StringComparison.OrdinalIgnoreCase)) return false;
        lastSystemAccent = next;
        currentAccent = next;
        return true;
    }

    private static string CalculateAccent() => settings.Mode switch
    {
        1 => ReadWindowsAccent(),
        2 => RandomAccent(),
        3 => settings.CustomAccent,
        _ => "#D8A91E"
    };

    private static string ReadWindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            object? raw = key?.GetValue("AccentColor");
            if (raw is int signed) return FromBgra(unchecked((uint)signed));
            if (raw is long wide) return FromBgra(unchecked((uint)wide));
        }
        catch { }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("ColorPrevalence") is int) { }
        }
        catch { }
        return "#6ABF40";
    }

    private static string FromBgra(uint value)
    {
        int red = (int)(value & 0xFF);
        int green = (int)((value >> 8) & 0xFF);
        int blue = (int)((value >> 16) & 0xFF);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static readonly string[] RandomColors =
    {
        "#4F8EDC", "#8B5CC7", "#D9577D", "#D87832", "#159B91",
        "#6AAB35", "#B64DA8", "#3976B9", "#C19A22", "#C94B42"
    };

    private static string RandomAccent()
    {
        var index = Random.Shared.Next(RandomColors.Length);
        return RandomColors[index];
    }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var parsed = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (parsed != null) return parsed;
            }
        }
        catch { }
        return new Settings();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
