using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class LocalAchievementService
{
    static readonly object gate = new();
    static readonly string folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GamesForWindowsLIVE2026");
    static readonly string file = Path.Combine(folder, "Achievements.json");
    static HashSet<string>? unlocked;

    static readonly Dictionary<string, AchievementEvent> catalog = new()
    {
        ["welcome"] = new() { Source = "BladesOverlay", Game = "Games for Windows LIVE 2026", Title = "¡Bienvenida al Guide!", Description = "Tu aventura empieza aquí.", Points = 5 },
        ["guide_open"] = new() { Source = "BladesOverlay", Game = "Games for Windows LIVE 2026", Title = "De vuelta al 2007", Description = "Abre el Guide clásico por primera vez.", Points = 10 },
        ["game_seen"] = new() { Source = "BladesOverlay", Game = "Games for Windows LIVE 2026", Title = "¡A jugar!", Description = "El Guide detectó una ventana de juego.", Points = 10 },
        ["profile"] = new() { Source = "BladesOverlay", Game = "Games for Windows LIVE 2026", Title = "Mi identidad, mi perfil", Description = "Personaliza tu perfil local.", Points = 5 },
        ["theme"] = new() { Source = "BladesOverlay", Game = "Games for Windows LIVE 2026", Title = "A mi manera", Description = "Guarda tu primer tema del Guide.", Points = 5 },
        ["library"] = new() { Source = "BladesOverlay", Game = "Games for Windows LIVE 2026", Title = "Mi colección", Description = "Abre la biblioteca local de juegos.", Points = 5 }
    };

    static HashSet<string> Load()
    {
        if (unlocked != null) return unlocked;
        try
        {
            Directory.CreateDirectory(folder);
            unlocked = File.Exists(file)
                ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(file)) ?? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch { unlocked = new HashSet<string>(StringComparer.Ordinal); }
        return unlocked;
    }

    public static AchievementEvent? Unlock(string id)
    {
        lock (gate)
        {
            if (!catalog.TryGetValue(id, out var achievement)) return null;
            var items = Load();
            if (!items.Add(id)) return null;
            try
            {
                Directory.CreateDirectory(folder);
                string temp = file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(items.OrderBy(x => x)));
                File.Move(temp, file, true);
            }
            catch { /* Keep the current session working if local persistence is unavailable. */ }
            return achievement;
        }
    }
}
