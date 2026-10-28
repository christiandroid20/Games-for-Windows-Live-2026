using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool LockWorkStation();

    internal static void ShowLocalFeatureInfo(Window? owner, string feature)
    {
        string text = $"{feature} forma parte de la interfaz recreada, pero esta compilación funciona en modo local y no está conectada a Xbox LIVE. No se mostrarán amigos, mensajes ni estados inventados.";
        const string caption = "Games for Windows LIVE 2026 · modo local";
        if (owner != null) MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    internal static void OpenMusicFolder() => Launch("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

    internal static void Launch(string target, string args = "")
    {
        try
        {
            var start = new ProcessStartInfo(target, args) { UseShellExecute = true };
            // Games expect to start in their own folder.
            if (File.Exists(target)) start.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            Process.Start(start);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el elemento.\n\n{target}\n{ex.Message}", "Games for Windows LIVE 2026", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [STAThread]
    static void Main()
    {
        // Persist startup/runtime failures so a disappearing overlay is diagnosable
        // even when launched from Explorer without a console.
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GamesForWindowsLIVE2026", "crash.log");
        void LogException(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 72)}{Environment.NewLine}");
            }
            catch { }
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            LogException("WPF DispatcherUnhandledException", e.Exception);
            e.Handled = true;
            MessageBox.Show(
                $"Ocurrió un error en el overlay. El detalle se guardó en:{Environment.NewLine}{logPath}{Environment.NewLine}{Environment.NewLine}{e.Exception.Message}",
                "Games for Windows LIVE 2026", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogException("AppDomain UnhandledException", ex);
            else LogException("AppDomain UnhandledException", new Exception(e.ExceptionObject?.ToString() ?? "Error desconocido"));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogException("TaskScheduler UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
        Overlay? overlay = null;
        var blades = new[]
        {
            // Player tab: gamer card, Community, media transport. Its entries are the "Configuración personal" sub-menu.
            new Blade("Jugador", "", new[]
            {
                new Entry("Cuenta de Windows", "Abrir la configuración del perfil de Windows", () => Launch("ms-settings:yourinfo")),
                new Entry("Editar perfil local", "Nombre visible e imagen del Guide", () => overlay?.EditProfile(), true),
                new Entry("Tema del Guide", "Color clásico, aleatorio, sistema o personalizado", () => overlay?.EditTheme(), true),
                new Entry("Diagnóstico de mandos", "Jugadores XInput y batería disponible", () => ControllerDiagnosticsWindow.Show(null)),
                new Entry("Configuración de Windows", "Abrir configuración de Windows", () => Launch("ms-settings:")),
                new Entry("Bloquear equipo", "Volver a la pantalla de bloqueo", () => LockWorkStation())
            }, true),
            new Blade("Juegos", "Tiendas y ejecutables locales", new[]
            {
                new Entry("Steam", "Abrir biblioteca de Steam", () => Launch("steam://open/main")),
                new Entry("Epic Games Launcher", "Abrir Epic Games", () => Launch("com.epicgames.launcher://")),
                new Entry("GOG GALAXY", "Abrir GOG GALAXY", () => Launch("goggalaxy://")),
                new Entry("Biblioteca local de juegos", "Explorador integrado · unidades, carpetas y .exe; elige sin salir del Guide", () => overlay?.OpenFileBrowser(), true)
            }),
            new Blade("Medios", "Accesos multimedia de Windows", new[]
            {
                new Entry("Música", "Abrir carpeta de música", () => Launch("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic))),
                new Entry("Vídeos", "Abrir carpeta de vídeos", () => Launch("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))),
                new Entry("Imágenes", "Abrir carpeta de imágenes", () => Launch("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))),
                new Entry("Captura de pantalla", "Abrir herramienta de recorte", () => Launch("ms-screenclip:"))
            }),
            new Blade("Sistema", "Herramientas · sin servicios propios", new[]
            {
                new Entry("Explorador de Windows", "Abrir el Explorador de archivos", () => Launch("explorer.exe")),
                new Entry("Administrador de tareas", "Procesos y rendimiento", () => Launch("taskmgr.exe")),
                new Entry("Monitor de recursos", "CPU, memoria y disco", () => Launch("resmon.exe")),
                new Entry("Sonido", "Dispositivos de reproducción", () => Launch("mmsys.cpl")),
                new Entry("Conexiones de red", "Adaptadores de red", () => Launch("ncpa.cpl")),
                new Entry("Panel de control", "Herramientas clásicas", () => Launch("control.exe")),
                new Entry("Salir", "Cerrar Games for Windows LIVE 2026", () => Application.Current.Shutdown())
            })
        };
        overlay = new Overlay(blades);
        app.MainWindow = overlay;
        // Show the Guide on launch while hotkey/controller activation is being validated.
        // This avoids a silent background process when Windows reserves Win+G for Game Bar.
        overlay.Show();
        app.Run();
    }
}
