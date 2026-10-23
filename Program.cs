using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

record Item(string Label, Action Run);
record Tab(string Title, Item[] Items, bool Profile = false);

class Overlay : Window
{
    const string Brand = "WINDOWS"; // cambia el texto de la placa inferior

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern int XInputGetStateEx(int i, out XState s);
    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

    readonly Tab[] data;
    readonly StackPanel body = new();
    readonly StackPanel tabsHost = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock clock = new() { FontSize = 16, Foreground = S("#3A3A3A"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
    readonly TranslateTransform slide = new();
    int b, i;
    ushort prev;

    static Color C(string h) => (Color)ColorConverter.ConvertFromString(h);
    static SolidColorBrush S(string h) => new(C(h));
    static LinearGradientBrush G(string a, string c) => new(C(a), C(c), 90);
    static TextBlock T(string s, double size, string fg = "#333333", FontWeight? w = null) =>
        new() { Text = s, FontSize = size, Foreground = S(fg), FontWeight = w ?? FontWeights.Normal, FontFamily = new FontFamily("Segoe UI") };
    static void Media(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

    public Overlay(Tab[] d)
    {
        data = d;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Topmost = true; ShowInTaskbar = false;
        WindowState = WindowState.Maximized;
        Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));

        var header = new Border { Height = 34, Background = G("#D4D4D4", "#ABABAB"), Child = clock };
        var footer = BuildFooter();
        DockPanel.SetDock(header, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        body.Margin = new Thickness(16, 12, 16, 0);
        var dock = new DockPanel(); dock.Children.Add(header); dock.Children.Add(footer); dock.Children.Add(body);
        var panel = new Border { Width = 400, Height = 540, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true,
                                 Background = G("#EEEEEE", "#CDCDCD"), CornerRadius = new CornerRadius(14, 0, 0, 14), Child = dock };

        var root = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left,
                                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(60, 50, 0, 0), RenderTransform = slide };
        root.Children.Add(panel); root.Children.Add(tabsHost);
        Content = root;
        KeyDown += OnKey;

        var h = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(h).AddHook(Hook);
        RegisterHotKey(h, 1, 0x4008, 0x47); // Win+G
        RegisterHotKey(h, 2, 0x4003, 0x47); // Ctrl+Alt+G

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        t.Tick += (_, _) => Poll(); t.Start();
        Refresh();
    }

    Border BuildFooter()
    {
        var u = new UniformGrid { Columns = 2, Margin = new Thickness(16, 6, 16, 6) };
        u.Children.Add(Hint("Y", "#E6B800", "Escritorio"));
        u.Children.Add(Hint("B", "#D32F2F", "Atrás"));
        u.Children.Add(Hint("X", "#2F6FD3", "Bloquear"));
        u.Children.Add(Hint("A", "#3BA52A", "Seleccionar"));
        return new Border { Height = 64, Background = G("#4A4A4A", "#2B2B2B"), Child = u };
    }

    static StackPanel Hint(string k, string col, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ball = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = S(col), Child = T(k, 12, "#FFFFFF", FontWeights.Bold) };
        ((TextBlock)ball.Child).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)ball.Child).VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(ball);
        var tx = T(text, 14, "#FFFFFF"); tx.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(tx);
        return sp;
    }

    Border Card()
    {
        var av = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(4), Background = G("#F6B3CE", "#D9739E"),
                              Child = T(Environment.UserName.Substring(0, 1).ToUpper(), 24, "#FFFFFF", FontWeights.Bold) };
        ((TextBlock)av.Child).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)av.Child).VerticalAlignment = VerticalAlignment.Center;
        var l1 = new Grid(); l1.Children.Add(T(Environment.UserName, 15, "#222222", FontWeights.SemiBold));
        var r1 = T(Environment.MachineName, 13, "#666666"); r1.HorizontalAlignment = HorizontalAlignment.Right; l1.Children.Add(r1);
        var l2 = new Grid(); l2.Children.Add(T("Estado", 14));
        var r2 = T("En línea", 14); r2.HorizontalAlignment = HorizontalAlignment.Right; l2.Children.Add(r2);
        var col = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(l1); col.Children.Add(l2);
        var dp = new DockPanel(); DockPanel.SetDock(av, Dock.Left); dp.Children.Add(av); dp.Children.Add(col);
        return new Border { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(4),
                            Background = S("#F8F8F8"), BorderBrush = S("#B5B5B5"), BorderThickness = new Thickness(1), Child = dp };
    }

    Border Row(string text, bool sel) => new()
    {
        Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(3),
        Background = sel ? G("#9BE36B", "#4FB52F") : Brushes.Transparent, Child = T(text, 18, sel ? "#FFFFFF" : "#333333")
    };

    StackPanel MediaRow()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (g, vk) in new[] { ("|◀", (byte)0xB1), ("▶", (byte)0xB3), ("▶|", (byte)0xB0), ("■", (byte)0xB2), ("♪", (byte)0xAD) })
        {
            var bd = new Border { Width = 58, Height = 30, Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(3),
                                  Background = G("#F6F6F6", "#D4D4D4"), BorderBrush = S("#A8A8A8"), BorderThickness = new Thickness(1), Child = T(g, 14, "#555555") };
            ((TextBlock)bd.Child).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)bd.Child).VerticalAlignment = VerticalAlignment.Center;
            bd.MouseLeftButtonUp += (_, _) => Media(vk);
            sp.Children.Add(bd);
        }
        return sp;
    }

    Border Plate()
    {
        var tx = T(Brand, 30, "#707070", FontWeights.Bold); tx.HorizontalAlignment = HorizontalAlignment.Center; tx.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Height = 64, Margin = new Thickness(0, 14, 0, 0), CornerRadius = new CornerRadius(6),
                            Background = G("#FFFFFF", "#E2E2E2"), BorderBrush = S("#B5B5B5"), BorderThickness = new Thickness(1), Child = tx };
    }

    Border TabStrip(string title, bool active)
    {
        var g = new Grid();
        if (active) g.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = G("#9BE36B", "#2C8A1A"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0) });
        var tx = T(title, 18, active ? "#333333" : "#8A8A8A", active ? FontWeights.SemiBold : FontWeights.Normal);
        tx.LayoutTransform = new RotateTransform(90); tx.HorizontalAlignment = HorizontalAlignment.Center; tx.VerticalAlignment = VerticalAlignment.Top;
        tx.Margin = new Thickness(0, active ? 48 : 20, 0, 0); g.Children.Add(tx);
        return new Border { Width = active ? 38 : 34, Height = active ? 540 : 520, VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(active ? 0 : -4, active ? 0 : 10, 0, 0), CornerRadius = new CornerRadius(0, 10, 10, 0),
                            Background = active ? G("#E4E4E4", "#BBBBBB") : G("#4A4A4A", "#242424"), Child = g };
    }

    void Refresh()
    {
        var t = data[b];
        body.Children.Clear();
        if (t.Profile) body.Children.Add(Card());
        for (int j = 0; j < t.Items.Length; j++) body.Children.Add(Row(t.Items[j].Label, j == i));
        if (t.Profile) body.Children.Add(MediaRow());
        body.Children.Add(Plate());
        tabsHost.Children.Clear();
        tabsHost.Children.Add(TabStrip(t.Title, true));
        for (int k = 0; k < data.Length; k++) if (k != b) tabsHost.Children.Add(TabStrip(data[k].Title, false));
    }

    IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312) { Toggle(); handled = true; }
        return IntPtr.Zero;
    }

    void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        clock.Text = DateTime.Now.ToString("hh:mm tt");
        Show(); Activate(); Focus();
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-460, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase() });
    }

    void Poll()
    {
        try
        {
            if (IsVisible) clock.Text = DateTime.Now.ToString("hh:mm tt");
            if (XInputGetStateEx(0, out var s) != 0) return;
            var press = (ushort)(s.Buttons & ~prev); prev = s.Buttons;
            if ((press & 0x0400) != 0) { Toggle(); return; } // Guide
            if (!IsVisible) return;
            if ((press & 0x0004) != 0 || (press & 0x0100) != 0) Move(-1, 0); // izq / LB
            if ((press & 0x0008) != 0 || (press & 0x0200) != 0) Move(1, 0);  // der / RB
            if ((press & 0x0001) != 0) Move(0, -1);
            if ((press & 0x0002) != 0) Move(0, 1);
            if ((press & 0x1000) != 0) Exec();
            if ((press & 0x2000) != 0 || (press & 0x8000) != 0) Hide();
            if ((press & 0x4000) != 0) { Hide(); LockWorkStation(); }
        }
        catch { }
    }

    void OnKey(object s, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Move(-1, 0); break;
            case Key.Right: Move(1, 0); break;
            case Key.Up: Move(0, -1); break;
            case Key.Down: Move(0, 1); break;
            case Key.Enter: Exec(); break;
            case Key.Escape: Hide(); break;
        }
    }

    void Move(int dx, int dy)
    {
        if (dx != 0) { b = (b + dx + data.Length) % data.Length; i = 0; }
        int n = data[b].Items.Length;
        if (dy != 0) i = (i + dy + n) % n;
        Refresh();
    }

    void Exec() { var a = data[b].Items[i].Run; Hide(); a(); }
}

static class Program
{
    static void Go(string f, string a = "")
    {
        try { Process.Start(new ProcessStartInfo(f, a) { UseShellExecute = true }); } catch { }
    }

    [STAThread]
    static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var tabs = new[]
        {
            new Tab(Environment.UserName, new[] {
                new Item("Configuración personal", () => Go("ms-settings:")),
                new Item("Seleccionar música", () => Go("shell:My Music")),
                new Item("Explorador de archivos", () => Go("explorer")) }, true),
            new Tab("juegos", new[] {
                new Item("Administrador de tareas", () => Go("taskmgr")),
                new Item("Monitor de recursos", () => Go("resmon")),
                new Item("Captura de pantalla", () => Go("ms-screenclip:")) }),
            new Tab("medios", new[] {
                new Item("Videos", () => Go("shell:My Video")),
                new Item("Música", () => Go("shell:My Music")),
                new Item("Imágenes", () => Go("shell:My Pictures")) }),
            new Tab("sistema", new[] {
                new Item("Sonido", () => Go("mmsys.cpl")),
                new Item("Red", () => Go("ncpa.cpl")),
                new Item("Panel de control", () => Go("control")),
                new Item("Reiniciar", () => Go("shutdown", "/r /t 0")),
                new Item("Apagar", () => Go("shutdown", "/s /t 0")),
                new Item("Cerrar overlay", () => Application.Current.Shutdown()) }),
        };
        var o = new Overlay(tabs);
        app.Run();
    }
}
