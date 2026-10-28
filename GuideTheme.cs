using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;

/// <summary>
/// Persisted Guide appearance. Mode 0 preserves the original gold; mode 4 recreates the metallic grey Guide.
/// Modes 1-3 re-colour the chrome through <see cref="Tint"/>, keeping lightness and relative saturation.
/// </summary>
internal static class GuideTheme
{
    private sealed class Settings
    {
        public int Mode { get; set; } = 4;
        public string CustomAccent { get; set; } = "#6ABF40";
    }

    private static readonly Regex HexColor = new("^#([0-9a-fA-F]{6})$");

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GamesForWindowsLIVE2026", "guide-theme.json");

    private static Settings settings = Load();
    private static string lastSystemAccent = ReadWindowsAccent();
    private static string currentAccent = CalculateAccent();

    // 0 = classic gold, 1 = Windows accent, 2 = random, 3 = custom, 4 = metallic grey (default).
    public static int Mode => settings.Mode;
    public static string CustomAccent => settings.CustomAccent;
    public static string CurrentAccent => currentAccent;

    /// <summary>Returns the colour unchanged in classic mode, otherwise re-hued to the accent.</summary>
    public static Color Tint(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (settings.Mode == 0) return color;
        if (settings.Mode == 4)
        {
            // Neutral metallic finish: preserve each source colour's perceived brightness.
            byte grey = (byte)Math.Clamp(Math.Round(color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722), 0, 255);
            return Color.FromArgb(color.A, grey, grey, grey);
        }
        var accent = (Color)ColorConverter.ConvertFromString(currentAccent);
        ToHsl(accent, out double hue, out double accentSat, out _);
        ToHsl(color, out _, out double sat, out double light);
        double scale = Math.Clamp(accentSat / 0.9, 0.3, 1.1);
        return FromHsl(hue, Math.Clamp(sat * scale, 0, 1), light, color.A);
    }

    public static void SetMode(int mode, string customAccent)
    {
        settings.Mode = Math.Clamp(mode, 0, 4);
        if (!string.IsNullOrWhiteSpace(customAccent) && HexColor.IsMatch(customAccent))
            settings.CustomAccent = customAccent.ToUpperInvariant();
        currentAccent = settings.Mode == 2 ? RandomAccent() : CalculateAccent();
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

    private static string RandomAccent() => RandomColors[Random.Shared.Next(RandomColors.Length)];

    private static void ToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        double d = max - min;
        if (d < 1e-9) { h = 0; s = 0; return; }
        s = d / (1 - Math.Abs(2 * l - 1));
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60;
    }

    private static Color FromHsl(double h, double s, double l, byte alpha)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = (h % 360) / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        double m = l - c / 2;
        double r, g, b;
        if (hp < 1) { r = c; g = x; b = 0; }
        else if (hp < 2) { r = x; g = c; b = 0; }
        else if (hp < 3) { r = 0; g = c; b = x; }
        else if (hp < 4) { r = 0; g = x; b = c; }
        else if (hp < 5) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(alpha, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var parsed = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (parsed != null)
                {
                    // A hand-edited or damaged file must never stop the Guide from starting.
                    parsed.Mode = Math.Clamp(parsed.Mode, 0, 4);
                    if (string.IsNullOrWhiteSpace(parsed.CustomAccent) || !HexColor.IsMatch(parsed.CustomAccent))
                        parsed.CustomAccent = "#6ABF40";
                    return parsed;
                }
            }
        }
        catch { }
        return new Settings { Mode = 4 };
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
