using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Local-only IPC receiver for achievement events. It accepts clients only through a Windows
/// named pipe; no TCP port or external service is opened. Providers can be added behind this contract.
/// </summary>
internal sealed class AchievementBridge : IDisposable
{
    internal const string PipeName = "BladesOverlay.AchievementEvents";
    readonly CancellationTokenSource stop = new();
    Task? listener;

    public event Action<AchievementEvent>? AchievementReceived;

    public void Start()
    {
        if (listener != null) return;
        listener = Task.Run(() => ListenAsync(stop.Token));
    }

    async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe);
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line) || line.Length > 16384) continue;
                AchievementEvent? item = JsonSerializer.Deserialize<AchievementEvent>(line,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (item == null || string.IsNullOrWhiteSpace(item.Title)) continue;

                item = item with
                {
                    Source = Clean(item.Source, 32, "Test"),
                    Game = Clean(item.Game, 100, "Juego"),
                    Title = Clean(item.Title, 120, "Logro desbloqueado"),
                    Description = Clean(item.Description, 240, "Has conseguido un logro."),
                    Points = Math.Clamp(item.Points, 0, 100000)
                };
                AchievementReceived?.Invoke(item);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(150, CancellationToken.None); }
            catch (JsonException) { }
            catch (ObjectDisposedException) { break; }
        }
    }

    static string Clean(string? value, int max, string fallback)
    {
        string clean = (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean.Length > max ? clean[..max] : clean;
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }
}

internal sealed record AchievementEvent
{
    public string Source { get; init; } = "Test";
    public string Game { get; init; } = "Juego";
    public string Title { get; init; } = "Logro desbloqueado";
    public string Description { get; init; } = "Has conseguido un logro.";
    public int Points { get; init; }
}
