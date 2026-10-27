using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// RetroAchievements API client for catalog and per-user game progress.
/// This class is deliberately separate from the UI: it does not claim live unlock events.
/// Credentials are stored in the current user's Local AppData, never in the repository.
/// </summary>
internal sealed class RetroAchievementsCatalogProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GamesForWindowsLIVE2026");
    private static readonly string SettingsPath = Path.Combine(Folder, "retroachievements.json");
    private static readonly string CachePath = Path.Combine(Folder, "retroachievements-cache");

    private sealed class Settings
    {
        public string Username { get; set; } = "";
        public string ApiKey { get; set; } = "";
    }

    private sealed class GameListDto
    {
        [JsonPropertyName("ID")] public int Id { get; set; }
        [JsonPropertyName("Title")] public string Title { get; set; } = "";
        [JsonPropertyName("ConsoleID")] public int ConsoleId { get; set; }
        [JsonPropertyName("ConsoleName")] public string ConsoleName { get; set; } = "";
        [JsonPropertyName("ImageIcon")] public string? ImageIcon { get; set; }
        [JsonPropertyName("NumAchievements")] public int NumAchievements { get; set; }
        [JsonPropertyName("Points")] public int Points { get; set; }
    }

    private sealed class GameInfoDto
    {
        [JsonPropertyName("ID")] public int Id { get; set; }
        [JsonPropertyName("Title")] public string Title { get; set; } = "";
        [JsonPropertyName("ConsoleID")] public int ConsoleId { get; set; }
        [JsonPropertyName("ConsoleName")] public string ConsoleName { get; set; } = "";
        [JsonPropertyName("ImageIcon")] public string? ImageIcon { get; set; }
        [JsonPropertyName("NumAchievements")] public int NumAchievements { get; set; }
        [JsonPropertyName("Points")] public int Points { get; set; }
        [JsonPropertyName("NumAwardedToUser")] public int NumAwardedToUser { get; set; }
        [JsonPropertyName("NumAwardedToUserHardcore")] public int NumAwardedToUserHardcore { get; set; }
        [JsonPropertyName("UserCompletion")] public string? UserCompletion { get; set; }
        [JsonPropertyName("UserCompletionHardcore")] public string? UserCompletionHardcore { get; set; }
    }

    public sealed record GameMetadata(
        int Id, string Title, int ConsoleId, string ConsoleName,
        string? ImageIcon, int AchievementCount, int Points);

    public sealed record UserGameProgress(
        int Id, string Title, string ConsoleName, int AchievementCount, int Points,
        int Unlocked, int UnlockedHardcore, string? Completion, string? CompletionHardcore)
    {
        public bool IsCompleted => AchievementCount > 0 && Unlocked >= AchievementCount;
    }

    public bool IsConfigured
    {
        get
        {
            var settings = LoadSettings();
            return !string.IsNullOrWhiteSpace(settings.Username) &&
                   !string.IsNullOrWhiteSpace(settings.ApiKey);
        }
    }

    public void Configure(string username, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("El nombre de usuario de RetroAchievements es obligatorio.", nameof(username));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("La clave API de RetroAchievements es obligatoria.", nameof(apiKey));

        Directory.CreateDirectory(Folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
            new Settings { Username = username.Trim(), ApiKey = apiKey.Trim() },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Returns games that have a RetroAchievements catalog entry for the given console ID.</summary>
    public async Task<IReadOnlyList<GameMetadata>> GetGamesForConsoleAsync(
        int consoleId, CancellationToken cancellationToken = default)
    {
        if (consoleId <= 0) throw new ArgumentOutOfRangeException(nameof(consoleId));
        var settings = RequireSettings();
        string cacheName = "console-" + consoleId;
        var cached = ReadCache<List<GameListDto>>(cacheName);
        if (cached != null) return cached.Select(ToMetadata).ToArray();

        string url = "https://retroachievements.org/API/API_GetGameList.php" +
                     "?z=" + Uri.EscapeDataString(settings.Username) +
                     "&y=" + Uri.EscapeDataString(settings.ApiKey) +
                     "&i=" + consoleId;
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var games = await response.Content.ReadFromJsonAsync<List<GameListDto>>(
            cancellationToken: cancellationToken).ConfigureAwait(false) ?? new List<GameListDto>();
        WriteCache(cacheName, games);
        return games.Select(ToMetadata).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Gets the authenticated user's progress for one game. This is a snapshot, not a live
    /// unlock-event stream; callers must not turn it into toast notifications without a verified delta.
    /// </summary>
    public async Task<UserGameProgress> GetUserGameProgressAsync(
        int gameId, CancellationToken cancellationToken = default)
    {
        if (gameId <= 0) throw new ArgumentOutOfRangeException(nameof(gameId));
        var settings = RequireSettings();
        string url = "https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php" +
                     "?z=" + Uri.EscapeDataString(settings.Username) +
                     "&y=" + Uri.EscapeDataString(settings.ApiKey) +
                     "&g=" + gameId;
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var game = await response.Content.ReadFromJsonAsync<GameInfoDto>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("RetroAchievements devolvió una respuesta vacía.");

        return new UserGameProgress(
            game.Id, game.Title, game.ConsoleName, game.NumAchievements, game.Points,
            game.NumAwardedToUser, game.NumAwardedToUserHardcore,
            game.UserCompletion, game.UserCompletionHardcore);
    }

    private static Settings RequireSettings()
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                "RetroAchievements no está configurado. Guarda tu nombre de usuario y clave API en la configuración local de proveedores.");
        return settings;
    }

    private static Settings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    private static T? ReadCache<T>(string name) where T : class
    {
        try
        {
            string path = Path.Combine(CachePath, name + ".json");
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-12))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch { }
        return null;
    }

    private static void WriteCache<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(CachePath);
            string path = Path.Combine(CachePath, name + ".json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, path, true);
        }
        catch { /* Cache failures must not break API access. */ }
    }

    private static GameMetadata ToMetadata(GameListDto game) =>
        new(game.Id, game.Title, game.ConsoleId, game.ConsoleName,
            game.ImageIcon, game.NumAchievements, game.Points);
}
