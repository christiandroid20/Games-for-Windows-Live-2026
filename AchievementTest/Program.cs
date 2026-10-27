using System.IO.Pipes;
using System.Text;
using System.Text.Json;

const string pipeName = "BladesOverlay.AchievementEvents";
var achievement = new
{
    source = "Prueba local",
    game = "GTA: San Andreas",
    title = "¡A toda máquina!",
    description = "Notificación de prueba de BladesOverlay.",
    points = 20
};

try
{
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None);
    pipe.Connect(1800);
    using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
    await writer.WriteLineAsync(JsonSerializer.Serialize(achievement));
    Console.WriteLine("Evento de prueba enviado a BladesOverlay.");
}
catch (TimeoutException)
{
    Console.Error.WriteLine("No se encontró BladesOverlay. Abre primero el Guide y vuelve a ejecutar esta herramienta.");
    Environment.ExitCode = 2;
}
catch (IOException ex)
{
    Console.Error.WriteLine("No se pudo enviar la notificación: " + ex.Message);
    Environment.ExitCode = 3;
}
