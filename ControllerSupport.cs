using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

internal struct ControllerState
{
    public ushort Buttons;
}

internal interface IControllerProvider
{
    bool TryGetState(int userIndex, out ControllerState state);
}

internal sealed class XInputControllerProvider : IControllerProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint userIndex, out XINPUT_STATE state);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetStateLegacy(uint userIndex, out XINPUT_STATE state);

    public bool TryGetState(int userIndex, out ControllerState state)
    {
        state = default;
        if (userIndex < 0 || userIndex > 3) return false;
        XINPUT_STATE native;
        uint result;
        try { result = XInputGetState14((uint)userIndex, out native); }
        catch (DllNotFoundException)
        {
            try { result = XInputGetStateLegacy((uint)userIndex, out native); }
            catch (DllNotFoundException) { return false; }
        }
        if (result != 0) return false;
        state.Buttons = native.Gamepad.wButtons;
        return true;
    }
}

internal static class ControllerDiagnosticsWindow
{
    public static void Show(Window owner)
    {
        var window = new Window
        {
            Title = "Diagnóstico de mandos",
            Width = 390, Height = 230, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Mandos XInput conectados", FontSize = 18, Margin = new Thickness(0, 0, 0, 10) });
        var status = new TextBlock { Text = "", TextWrapping = TextWrapping.Wrap };
        var provider = new XInputControllerProvider();
        int connected = 0;
        for (int i = 0; i < 4; i++)
        {
            if (provider.TryGetState(i, out _))
            {
                connected++;
                status.Text += $"Jugador {i + 1}: conectado\n";
            }
        }
        if (connected == 0) status.Text = "No se detectaron mandos XInput. Conecta un mando compatible y vuelve a abrir esta ventana.";
        panel.Children.Add(status);
        var close = new Button { Content = "Cerrar", Width = 90, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.Content = panel;
        window.ShowDialog();
    }
}

internal static class WindowsProfileService
{
    private sealed class Profile
    {
        public string DisplayName { get; set; } = "";
        public string AvatarPath { get; set; } = "";
    }

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamesForWindowsLIVE2026");
    private static readonly string ProfilePath = Path.Combine(Folder, "profile.json");

    private static Profile Load()
    {
        try
        {
            if (File.Exists(ProfilePath))
                return JsonSerializer.Deserialize<Profile>(File.ReadAllText(ProfilePath)) ?? new Profile();
        }
        catch { }
        return new Profile();
    }

    private static void Save(Profile profile)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string GetWindowsDisplayName() =>
        Environment.GetEnvironmentVariable("USERNAME") ?? Environment.UserName;

    public static string GetDisplayName()
    {
        string value = Load().DisplayName.Trim();
        return value.Length == 0 ? GetWindowsDisplayName() : value;
    }

    public static string GetInitials()
    {
        string[] parts = GetDisplayName().Split(new[] { ' ', '\\', '/', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "G";
        return parts.Length == 1 ? parts[0].Substring(0, 1).ToUpperInvariant()
            : (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
    }

    public static string GetAvatarPath()
    {
        string path = Load().AvatarPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : "";
    }

    public static void SetDisplayName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length > 32) throw new ArgumentException("El nombre visible no puede superar 32 caracteres.");
        if (name.Length == 0) { ResetDisplayName(); return; }
        var p = Load(); p.DisplayName = name; Save(p);
    }

    public static void ResetDisplayName()
    {
        var p = Load(); p.DisplayName = ""; Save(p);
    }

    public static void SetAvatar(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("No se encontró la imagen seleccionada.", path);
        var p = Load(); p.AvatarPath = path; Save(p);
    }

    public static void ResetAvatar()
    {
        var p = Load(); p.AvatarPath = ""; Save(p);
    }
}
