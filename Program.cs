using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

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

    static void PickGame()
    {
        var dialog = new OpenFileDialog { Title = "Selecciona el ejecutable del juego", Filter = "Aplicaciones (*.exe)|*.exe|Todos los archivos (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog() == true) Launch(dialog.FileName);
    }

    [STAThread]
    static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
                new Entry("Explorar archivos de juegos", "Explorador integrado · unidades, carpetas y .exe", () => overlay?.OpenFileBrowser(), true),
                new Entry("Elegir ejecutable…", "Seleccionar un .exe con el diálogo de Windows", PickGame)
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
        app.Run();
    }
}
