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
/// Optional metadata provider. RAWG supplies game/achievement descriptions and artwork;
/// it does not report this user's unlock state. The API key is stored only in Local AppData.
/// </summary>
internal sealed class RawgCatalogProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GamesForWindowsLIVE2026");
    private static readonly string KeyPath = Path.Combine(Folder, "providers.json");
    private static readonly string CachePath = Path.Combine(Folder, "rawg-cache");

    private sealed class ProviderSettings
    {
        public string RawgApiKey { get; set; } = "";
    }

    private sealed class GameSearchResponse
    {
        [JsonPropertyName("results")] public List<GameDto> Results { get; set; } = new();
    }

    private sealed class GameDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("background_image")] public string? Image { get; set; }
        [JsonPropertyName("released")] public string? Released { get; set; }
    }

    private sealed class AchievementResponse
    {
        [JsonPropertyName("results")] public List<AchievementDto> Results { get; set; } = new();
        [JsonPropertyName("next")] public string? Next { get; set; }
    }

    private sealed class AchievementDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("image")] public string? Image { get; set; }
        [JsonPropertyName("percent")] public string? Percent { get; set; }
    }

    public sealed record GameMetadata(int Id, string Name, string Slug, string? Image, string? Released);
    public sealed record AchievementMetadata(string ProviderId, string Name, string Description, string? Image, string? GlobalPercent);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(LoadSettings().RawgApiKey);

    /// <summary>Stores a user's API key locally. Never commit this file to source control.</summary>
    public void Configure(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("La clave API no puede estar vacía.", nameof(apiKey));
        Directory.CreateDirectory(Folder);
        File.WriteAllText(KeyPath, JsonSerializer.Serialize(
            new ProviderSettings { RawgApiKey = apiKey.Trim() },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<IReadOnlyList<GameMetadata>> SearchGamesAsync(
        string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<GameMetadata>();
        string key = RequireKey();
        string cacheKey = "games-" + SafeCacheName(query);
        var cached = ReadCache<GameSearchResponse>(cacheKey);
        if (cached != null) return cached.Results.Select(ToMetadata).ToArray();

        string url = "https://api.rawg.io/api/games?key=" + Uri.EscapeDataString(key) +
                     "&search=" + Uri.EscapeDataString(query.Trim()) + "&page_size=20";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GameSearchResponse>(cancellationToken: cancellationToken)
                     .ConfigureAwait(false) ?? new GameSearchResponse();
        WriteCache(cacheKey, result);
        return result.Results.Select(ToMetadata).ToArray();
    }

    public async Task<IReadOnlyList<AchievementMetadata>> GetAchievementsAsync(
        int rawgGameId, CancellationToken cancellationToken = default)
    {
        if (rawgGameId <= 0) throw new ArgumentOutOfRangeException(nameof(rawgGameId));
        string key = RequireKey();
        string cacheKey = "achievements-" + rawgGameId;
        var cached = ReadCache<AchievementResponse>(cacheKey);
        if (cached != null) return cached.Results.Select(ToMetadata).ToArray();

        string url = $"https://api.rawg.io/api/games/{rawgGameId}/achievements?key={Uri.EscapeDataString(key)}&page_size=40";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AchievementResponse>(cancellationToken: cancellationToken)
                     .ConfigureAwait(false) ?? new AchievementResponse();
        WriteCache(cacheKey, result);
        return result.Results.Select(ToMetadata).ToArray();
    }

    private string RequireKey()
    {
        string key = LoadSettings().RawgApiKey.Trim();
        if (key.Length == 0)
            throw new InvalidOperationException(
                "RAWG no está configurado. Guarda tu clave API en la configuración de proveedores del usuario.");
        return key;
    }

    private static ProviderSettings LoadSettings()
    {
        try
        {
            if (File.Exists(KeyPath))
                return JsonSerializer.Deserialize<ProviderSettings>(File.ReadAllText(KeyPath)) ?? new ProviderSettings();
        }
        catch { }
        return new ProviderSettings();
    }

    private static T? ReadCache<T>(string name) where T : class
    {
        try
        {
            string path = Path.Combine(CachePath, name + ".json");
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-7))
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
        catch { /* A cache failure must not break the catalog request. */ }
    }

    private static string SafeCacheName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string clean = new(value.ToLowerInvariant().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Length > 80 ? clean[..80] : clean;
    }

    private static GameMetadata ToMetadata(GameDto game) =>
        new(game.Id, game.Name, game.Slug, game.Image, game.Released);

    private static AchievementMetadata ToMetadata(AchievementDto achievement) =>
        new(achievement.Id.ToString(), achievement.Name, achievement.Description,
            achievement.Image, achievement.Percent);
}
