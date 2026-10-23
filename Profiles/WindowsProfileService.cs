using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal sealed class LocalProfileSettings
{
    public string? DisplayName { get; set; }
    public string? CustomAvatarPath { get; set; }
}

internal static class WindowsProfileService
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GamesForWindowsLive2026", "Profile");
    private static readonly string SettingsPath = Path.Combine(Root, "profile.json");

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserNameExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder nameBuffer, ref uint size);

    private static LocalProfileSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<LocalProfileSettings>(File.ReadAllText(SettingsPath)) ?? new LocalProfileSettings();
        }
        catch { }
        return new LocalProfileSettings();
    }

    private static void Save(LocalProfileSettings settings)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string GetWindowsDisplayName()
    {
        try
        {
            uint size = 256;
            var buffer = new StringBuilder((int)size);
            // EXTENDED_NAME_FORMAT.NameDisplay = 3. Local accounts may not expose this value.
            if (GetUserNameEx(3, buffer, ref size) && !string.IsNullOrWhiteSpace(buffer.ToString()))
                return buffer.ToString();
        }
        catch { }
        return Environment.UserName;
    }

    public static string GetDisplayName()
    {
        var saved = Load().DisplayName;
        return string.IsNullOrWhiteSpace(saved) ? GetWindowsDisplayName() : saved.Trim();
    }

    public static void SetDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre del perfil no puede estar vacío.", nameof(name));
        var settings = Load();
        settings.DisplayName = name.Trim();
        Save(settings);
    }

    public static string? GetAvatarPath()
    {
        var saved = Load().CustomAvatarPath;
        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
            return saved;

        // Windows keeps recent account pictures here. The newest image is a best-effort
        // fallback; Windows does not expose one stable, documented path for every account type.
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "AccountPictures");
            if (Directory.Exists(folder))
            {
                var path = Directory.GetFiles(folder)
                    .Where(p => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (path != null) return path;
            }
        }
        catch { }
        return null;
    }

    public static void SetAvatar(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("No se encontró la imagen seleccionada.", sourcePath);

        Directory.CreateDirectory(Root);
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
            extension = ".png";
        var destination = Path.Combine(Root, "avatar" + extension.ToLowerInvariant());
        File.Copy(sourcePath, destination, true);

        var settings = Load();
        settings.CustomAvatarPath = destination;
        Save(settings);
    }

    public static void ResetAvatar()
    {
        var settings = Load();
        settings.CustomAvatarPath = null;
        Save(settings);
    }

    public static string GetInitials()
    {
        var words = GetDisplayName().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "G";
        return words.Length == 1
            ? words[0].Substring(0, 1).ToUpperInvariant()
            : (words[0].Substring(0, 1) + words[^1].Substring(0, 1)).ToUpperInvariant();
    }
}
