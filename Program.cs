using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

internal sealed record Entry(string Name, string Detail, Action Run);
internal sealed record Blade(string Name, string Subtitle, Entry[] Entries);

internal sealed class Overlay : Window
{
    const int Hotkey1 = 1, Hotkey2 = 2, WmHotkey = 0x312;
    const uint ModWin = 8, ModControl = 2, ModAlt = 1, ModNoRepeat = 0x4000;
    const ushort Guide = 0x0400;

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("xinput1_4.dll", EntryPoint = "#100", CallingConvention = CallingConvention.StdCall)]
    static extern int XInputGetStateEx(int index, out XState state);
    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

    readonly Blade[] blades;
    readonly Border[] bladeTabs;
    readonly StackPanel rows = new();
    readonly TextBlock title = new(), subtitle = new(), clock = new(), status = new();
    readonly TranslateTransform slide = new();
    readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer padTimer = new() { Interval = TimeSpan.FromMilliseconds(55), Priority = DispatcherPriority.Input };
    readonly ushort[] previous = new ushort[4];
    HwndSource source;
    int activeBlade, activeRow;
    bool hotkey1, hotkey2, guideUnavailable, animating;

    static Color C(string s) => (Color)ColorConverter.ConvertFromString(s);
    static SolidColorBrush B(string s) => new(C(s));
    static LinearGradientBrush G(string a, string b) => new(C(a), C(b), 90);
    static TextBlock T(string s, double size, string color = "#292929", FontWeight? weight = null) => new()
    {
        Text = s, FontSize = size, Foreground = B(color), FontWeight = weight ?? FontWeights.Normal,
        FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    public Overlay(Blade[] sourceBlades)
    {
        blades = sourceBlades;
        bladeTabs = new Border[blades.Length];
        Title = "Games for Windows LIVE 2026";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(218, 0, 0, 0));
        Topmost = true; ShowInTaskbar = false; WindowState = WindowState.Maximized;
        UseLayoutRounding = true; SnapsToDevicePixels = true;

        var panel = BuildPanel();
        panel.RenderTransform = slide;
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(panel); Content = host;

        PreviewKeyDown += KeyPressed;
        SourceInitialized += (_, _) => { source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source?.AddHook(Hook); };
        Loaded += (_, _) => RegisterHotkeys();
        Closed += (_, _) => Cleanup();
        clockTimer.Tick += (_, _) => clock.Text = DateTime.Now.ToString("h:mm tt");
        padTimer.Tick += (_, _) => PollPad();
        clockTimer.Start(); padTimer.Start();
        clock.Text = DateTime.Now.ToString("h:mm tt");
        DrawTabs(); DrawRows(); SetStatus();
    }

    Border BuildPanel()
    {
        var outer = new Border
        {
            Width = 920, Height = 570, Padding = new Thickness(7), CornerRadius = new CornerRadius(3),
            Background = G("#E5E5E5", "#A8A8A8"), BorderBrush = B("#FFFFFF"), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 26, ShadowDepth = 0, Opacity = .7 }
        };
        var dock = new DockPanel();
        var header = new Grid { Height = 54, Margin = new Thickness(0, 0, 0, 6), Background = G("#65B83F", "#2D781E") };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15, 0, 0, 0) };
        brand.Children.Add(T("X", 29, "#FFFFFF", FontWeights.Bold));
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        words.Children.Add(T("GAMES FOR WINDOWS", 18, "#FFFFFF", FontWeights.Bold));
        words.Children.Add(T("LIVE  •  CLASSIC BLADES EDITION", 10, "#E2F3D8", FontWeights.SemiBold));
        brand.Children.Add(words); header.Children.Add(brand);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        right.Children.Add(T("LOCAL OVERLAY", 10, "#E2F3D8", FontWeights.SemiBold));
        clock.FontSize = 16; clock.Foreground = B("#FFFFFF"); clock.Margin = new Thickness(18, 0, 0, 0);
        right.Children.Add(clock); Grid.SetColumn(right, 1); header.Children.Add(right);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Height = 48, Background = G("#505050", "#292929"), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(Hint("A", "#4BAA35", "Seleccionar"));
        footer.Children.Add(Hint("B", "#D54A42", "Cerrar"));
        footer.Children.Add(Hint("LB / RB", "#D6D6D6", "Cambiar blade"));
        footer.Children.Add(Hint("↑ ↓", "#D6D6D6", "Mover selección"));
        footer.Children.Add(T("  Win + G  ·  Guide", 12, "#E6E6E6"));
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition());
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        var left = new Grid { Margin = new Thickness(10, 12, 14, 12) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        title.FontSize = 26; title.FontWeight = FontWeights.Light; title.Foreground = B("#353535");
        left.Children.Add(title);
        subtitle.FontSize = 12; subtitle.Foreground = B("#666666"); subtitle.Margin = new Thickness(1, 0, 0, 13);
        Grid.SetRow(subtitle, 1); left.Children.Add(subtitle);
        var frame = new Border { Background = B("#F3F3F3"), BorderBrush = B("#B6B6B6"), BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = rows };
        Grid.SetRow(frame, 2); left.Children.Add(frame);
        status.FontSize = 11; status.Foreground = B("#5A5A5A"); status.Margin = new Thickness(1, 10, 0, 0);
        Grid.SetRow(status, 3); left.Children.Add(status);
        main.Children.Add(left);

        var rail = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        for (int n = 0; n < blades.Length; n++)
        {
            rail.ColumnDefinitions.Add(new ColumnDefinition());
            var tab = MakeBlade(blades[n], n);
            bladeTabs[n] = tab; Grid.SetColumn(tab, n); rail.Children.Add(tab);
        }
        Grid.SetColumn(rail, 1); main.Children.Add(rail); dock.Children.Add(main); outer.Child = dock;
        return outer;
    }

    static UIElement Hint(string key, string color, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        var label = T(key, 10, color == "#D6D6D6" ? "#303030" : "#FFFFFF", FontWeights.Bold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        row.Children.Add(new Border { MinWidth = 23, Height = 22, Padding = new Thickness(5, 0, 5, 0), Background = B(color), CornerRadius = new CornerRadius(11), Child = label });
        var caption = T(text, 11, "#EEEEEE"); caption.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(caption);
        return row;
    }

    Border MakeBlade(Blade blade, int index)
    {
        var label = T(blade.Name.ToUpperInvariant(), 14, "#F4F4F4", FontWeights.SemiBold);
        label.LayoutTransform = new RotateTransform(90);
        label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        var accent = new Border { Height = 7, VerticalAlignment = VerticalAlignment.Top, Background = G("#A5E57E", "#4B9F2B"), Visibility = Visibility.Collapsed };
        var layers = new Grid(); layers.Children.Add(label); layers.Children.Add(accent);
        var tab = new Border
        {
            Margin = new Thickness(index == 0 ? 0 : -2, index == 0 ? 0 : 12, 0, index == 0 ? 0 : 12),
            Background = G("#656565", "#292929"), BorderBrush = B("#858585"), BorderThickness = new Thickness(1, 1, 0, 1), Child = layers, Cursor = Cursors.Hand
        };
        tab.MouseLeftButtonUp += (_, _) => SelectBlade(index, true);
        return tab;
    }

    void DrawTabs()
    {
        for (int n = 0; n < blades.Length; n++)
        {
            bool selected = n == activeBlade;
            var tab = bladeTabs[n];
            tab.Margin = new Thickness(selected ? 0 : -2, selected ? 0 : 12, 0, selected ? 0 : 12);
            tab.Background = selected ? G("#F3F3F3", "#BEBEBE") : G("#656565", "#292929");
            tab.BorderBrush = B(selected ? "#FFFFFF" : "#858585");
            if (tab.Child is Grid layers)
            {
                ((Border)layers.Children[1]).Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (layers.Children[0] is TextBlock label) { label.Foreground = B(selected ? "#303030" : "#F4F4F4"); label.FontSize = selected ? 16 : 14; }
            }
        }
        title.Text = blades[activeBlade].Name; subtitle.Text = blades[activeBlade].Subtitle;
    }

    void DrawRows()
    {
        rows.Children.Clear();
        var entries = blades[activeBlade].Entries;
        if (entries.Length == 0) { rows.Children.Add(T("No hay elementos.", 14)); return; }
        activeRow = Math.Clamp(activeRow, 0, entries.Length - 1);
        for (int n = 0; n < entries.Length; n++)
        {
            int picked = n; bool selected = n == activeRow;
            var copy = new StackPanel { Margin = new Thickness(12, 4, 8, 4) };
            copy.Children.Add(T(entries[n].Name, 15, selected ? "#FFFFFF" : "#303030", selected ? FontWeights.SemiBold : FontWeights.Normal));
            copy.Children.Add(T(entries[n].Detail, 11, selected ? "#F1F7EC" : "#777777"));
            var stripe = new Border { Width = 4, Margin = new Thickness(0, 5, 0, 5), Background = B(selected ? "#8FD76C" : "#C8C8C8") };
            var dock = new DockPanel(); DockPanel.SetDock(stripe, Dock.Left); dock.Children.Add(stripe); dock.Children.Add(copy);
            var row = new Border
            {
                Height = 62, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(4, 0, 4, 0),
                Background = selected ? G("#72BD4D", "#398C28") : G("#FAFAFA", "#E5E5E5"),
                BorderBrush = B(selected ? "#2D7B20" : "#D1D1D1"), BorderThickness = new Thickness(1), Child = dock, Cursor = Cursors.Hand
            };
            row.MouseEnter += (_, _) => { activeRow = picked; DrawRows(); };
            row.MouseLeftButtonUp += (_, _) => { activeRow = picked; DrawRows(); ExecuteSelected(); };
            rows.Children.Add(row);
        }
    }

    void RegisterHotkeys()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        hotkey1 = RegisterHotKey(hwnd, Hotkey1, ModWin | ModNoRepeat, 0x47);
        hotkey2 = RegisterHotKey(hwnd, Hotkey2, ModControl | ModAlt | ModNoRepeat, 0x47);
        if (!hotkey1) status.Text = "Win+G está ocupado; prueba Ctrl+Alt+G.";
    }

    IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && (wParam.ToInt32() == Hotkey1 || wParam.ToInt32() == Hotkey2))
        { Toggle(); handled = true; }
        return IntPtr.Zero;
    }

    void Toggle()
    {
        if (animating) return;
        if (IsVisible) { CloseAnimated(); return; }
        animating = true; clock.Text = DateTime.Now.ToString("h:mm tt");
        Show(); Activate(); Focus(); Opacity = 0; slide.Y = -22;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var animation = new DoubleAnimation(-22, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => { animating = false; Focus(); };
        slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    void CloseAnimated()
    {
        if (!IsVisible || animating) return;
        animating = true;
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120)));
        var animation = new DoubleAnimation(slide.Y, -18, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        animation.Completed += (_, _) => { Hide(); Opacity = 1; slide.Y = 0; animating = false; };
        slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    void SelectBlade(int next, bool animate)
    {
        if (next == activeBlade || next < 0 || next >= blades.Length) return;
        activeBlade = next; activeRow = 0; DrawTabs(); DrawRows();
        if (animate) rows.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void Move(int dx, int dy)
    {
        if (!IsVisible || animating) return;
        if (dx != 0) { SelectBlade((activeBlade + dx + blades.Length) % blades.Length, true); return; }
        int count = blades[activeBlade].Entries.Length;
        if (count == 0) return;
        activeRow = (activeRow + dy + count) % count; DrawRows();
    }

    void KeyPressed(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Move(-1, 0); e.Handled = true; break;
            case Key.Right: Move(1, 0); e.Handled = true; break;
            case Key.Up: Move(0, -1); e.Handled = true; break;
            case Key.Down: Move(0, 1); e.Handled = true; break;
            case Key.Enter: ExecuteSelected(); e.Handled = true; break;
            case Key.Escape:
            case Key.Back: CloseAnimated(); e.Handled = true; break;
        }
    }

    void ExecuteSelected()
    {
        if (animating) return;
        var entries = blades[activeBlade].Entries;
        if (activeRow < 0 || activeRow >= entries.Length) return;
        Action action = entries[activeRow].Run;
        CloseAnimated();
        Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    void PollPad()
    {
        if (guideUnavailable) return;
        try
        {
            for (int user = 0; user < 4; user++)
            {
                if (XInputGetStateEx(user, out var state) != 0) { previous[user] = 0; continue; }
                ushort pressed = (ushort)(state.Buttons & ~previous[user]); previous[user] = state.Buttons;
                if ((pressed & Guide) != 0) { Toggle(); return; }
                if (!IsVisible || animating) continue;
                if ((pressed & 0x0100) != 0 || (pressed & 0x0004) != 0) Move(-1, 0);
                else if ((pressed & 0x0200) != 0 || (pressed & 0x0008) != 0) Move(1, 0);
                else if ((pressed & 0x0001) != 0) Move(0, -1);
                else if ((pressed & 0x0002) != 0) Move(0, 1);
                else if ((pressed & 0x1000) != 0) ExecuteSelected();
                else if ((pressed & 0x2000) != 0 || (pressed & 0x8000) != 0) CloseAnimated();
                else if ((pressed & 0x4000) != 0) { CloseAnimated(); LockWorkStation(); }
            }
        }
        catch (DllNotFoundException) { DisableGuide(); }
        catch (EntryPointNotFoundException) { DisableGuide(); }
        catch (BadImageFormatException) { DisableGuide(); }
    }

    void DisableGuide()
    {
        guideUnavailable = true;
        status.Text = "Guide no disponible; usa teclado o Ctrl+Alt+G.";
    }

    void SetStatus()
    {
        status.Text = guideUnavailable ? "Guide no disponible; usa teclado o Ctrl+Alt+G." : "Flechas / LB-RB · Enter / A abre · Esc / B cierra";
    }

    void Cleanup()
    {
        clockTimer.Stop(); padTimer.Stop();
        if (source != null) source.RemoveHook(Hook);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hotkey1) UnregisterHotKey(hwnd, Hotkey1);
        if (hotkey2) UnregisterHotKey(hwnd, Hotkey2);
    }
}

internal static class Program
{
    [DllImport("user32.dll")] static extern bool LockWorkStation();

    static void Launch(string target, string args = "")
    {
        try { Process.Start(new ProcessStartInfo(target, args) { UseShellExecute = true }); }
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
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var blades = new[]
        {
            new Blade("Perfil", "Tu sesión local · sin Xbox LIVE", new[]
            {
                new Entry(Environment.UserName, "Perfil local de Windows", () => Launch("ms-settings:yourinfo")),
                new Entry("Configuración", "Abrir configuración de Windows", () => Launch("ms-settings:")),
                new Entry("Explorador de archivos", "Acceder a unidades y carpetas", () => Launch("explorer.exe")),
                new Entry("Bloquear equipo", "Volver a la pantalla de bloqueo", () => LockWorkStation())
            }),
            new Blade("Juegos", "Tiendas y ejecutables locales", new[]
            {
                new Entry("Steam", "Abrir biblioteca de Steam", () => Launch("steam://open/main")),
                new Entry("Epic Games Launcher", "Abrir Epic Games", () => Launch("com.epicgames.launcher://")),
                new Entry("GOG GALAXY", "Abrir GOG GALAXY", () => Launch("goggalaxy://")),
                new Entry("Agregar / abrir juego…", "Elegir un ejecutable .exe", PickGame),
                new Entry("Carpeta de programas", "Abrir Archivos de programa", () => Launch("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
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
                new Entry("Administrador de tareas", "Procesos y rendimiento", () => Launch("taskmgr.exe")),
                new Entry("Monitor de recursos", "CPU, memoria y disco", () => Launch("resmon.exe")),
                new Entry("Sonido", "Dispositivos de reproducción", () => Launch("mmsys.cpl")),
                new Entry("Conexiones de red", "Adaptadores de red", () => Launch("ncpa.cpl")),
                new Entry("Panel de control", "Herramientas clásicas", () => Launch("control.exe")),
                new Entry("Salir", "Cerrar Games for Windows LIVE 2026", () => Application.Current.Shutdown())
            })
        };
        app.Run(new Overlay(blades));
    }
}
