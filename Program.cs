using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

internal sealed class GuideChrome : FrameworkElement
{
    public Brush Gold { get; set; } = new LinearGradientBrush(Color.FromRgb(244,195,29), Color.FromRgb(216,169,29), 0);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 80) return;
        double edge = Math.Min(w - 25, 245);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(edge - 12, 0), true, false);
            ctx.BezierTo(new Point(edge + 9, h * .12), new Point(edge - 20, h * .21), new Point(edge + 2, h * .33), true, false);
            ctx.BezierTo(new Point(edge + 13, h * .45), new Point(edge - 14, h * .53), new Point(edge - 2, h * .66), true, false);
            ctx.BezierTo(new Point(edge + 4, h * .81), new Point(edge - 27, h * .90), new Point(edge - 12, h), true, false);
            ctx.LineTo(new Point(0, h), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(Gold, new Pen(new SolidColorBrush(Color.FromRgb(102, 80, 17)), 2), geometry);
        dc.PushClip(geometry);
        var pattern = new Pen(new SolidColorBrush(Color.FromArgb(24, 118, 83, 0)), 1.5);
        for (double y = 12; y < h; y += 43)
        for (double x = 12; x < edge - 25; x += 39)
        {
            var p = new Point(x + (((int)(y / 43) % 2) * 14), y);
            dc.DrawEllipse(null, pattern, p, 8, 8);
            dc.DrawLine(pattern, new Point(p.X - 5, p.Y - 5), new Point(p.X + 5, p.Y + 5));
            dc.DrawLine(pattern, new Point(p.X + 5, p.Y - 5), new Point(p.X - 5, p.Y + 5));
        }
        dc.Pop();
    }
}

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
    readonly IControllerProvider controllerProvider = new XInputControllerProvider();
    readonly TextBlock profileName = new(), profileInitials = new();
    readonly Image profileImage = new();

    readonly Blade[] blades;
    readonly Border[] bladeTabs;
    readonly StackPanel rows = new();
    readonly TextBlock title = new(), subtitle = new(), clock = new(), status = new();
    readonly TranslateTransform slide = new();
    readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer padTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(55) };
    readonly ushort[] previous = new ushort[4];
    HwndSource? source;
    int activeBlade, activeRow;
    bool hotkey1, hotkey2, guideUnavailable, animating, browsingFiles;
    string? browserPath;
    readonly System.Collections.Generic.List<FileSystemInfo> browserItems = new();

    static Color C(string s) => (Color)ColorConverter.ConvertFromString(s);
    static string Accent => GuideTheme.CurrentAccent;
    static SolidColorBrush B(string s) => new(C(GuideTheme.ResolveColor(s)));
    static LinearGradientBrush G(string a, string b) => new(C(GuideTheme.ResolveColor(a)), C(GuideTheme.ResolveColor(b)), 90);
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
        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        source?.AddHook(Hook);
        RegisterHotkeys();
        Closed += (_, _) => Cleanup();
        clockTimer.Tick += (_, _) => { clock.Text = DateTime.Now.ToString("h:mm tt"); if (GuideTheme.RefreshAccent()) RefreshTheme(); };
        padTimer.Tick += (_, _) => PollPad();
        clockTimer.Start(); padTimer.Start();
        clock.Text = DateTime.Now.ToString("h:mm tt");
        DrawTabs(); DrawRows(); RefreshProfileHeader(); SetStatus();
    }

    Border BuildPanel()
    {
        // Compact Xbox 360 Guide overlay: a gold left rail over the dimmed desktop/game.
        var outer = new Border
        {
            Width = 690, Height = 700, Padding = new Thickness(0), CornerRadius = new CornerRadius(3, 24, 24, 3),
            Background = B("#17191B"), BorderBrush = B("#6F5A19"), BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 0, Opacity = .78 }
        };
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(238) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        outer.Child = shell;

        // The original Guide's signature is a curved gold silhouette, not a rectangular sidebar.
        var chrome = new GuideChrome { Width = 282, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        shell.Children.Add(chrome);
        var goldRail = new Grid { Background = Brushes.Transparent };
        var railContent = new DockPanel { Margin = new Thickness(13, 14, 24, 12) };
        var railTop = new StackPanel();
        railTop.Children.Add(T("XBOX 360", 13, "#604A08", FontWeights.Bold));
        railTop.Children.Add(T("GUIDE", 29, "#342A0C", FontWeights.Bold));
        railTop.Children.Add(T("QUICK MENU", 9, "#68500B", FontWeights.SemiBold));
        var profile = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 12), Cursor = Cursors.Hand };
        var avatar = new Grid { Width = 42, Height = 42, Margin = new Thickness(0, 0, 8, 0) };
        profileImage.Width = 42; profileImage.Height = 42; profileImage.Stretch = Stretch.UniformToFill;
        profileImage.Visibility = Visibility.Collapsed;
        profileInitials.Text = WindowsProfileService.GetInitials();
        profileInitials.FontSize = 11; profileInitials.FontWeight = FontWeights.Bold;
        profileInitials.Foreground = B("#FFFFFF"); profileInitials.Background = B("#5DAA37");
        profileInitials.TextAlignment = TextAlignment.Center;
        avatar.Children.Add(profileImage); avatar.Children.Add(profileInitials);
        profile.Children.Add(new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), ClipToBounds = true, Child = avatar, BorderBrush = B("#FFF0A1"), BorderThickness = new Thickness(2) });
        var profileText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        profileName.FontSize = 13; profileName.Foreground = B("#352A0B"); profileName.FontWeight = FontWeights.Bold;
        profileName.MaxWidth = 105; profileName.TextTrimming = TextTrimming.CharacterEllipsis;
        profileText.Children.Add(profileName);
        profileText.Children.Add(T("0 G", 11, "#665314"));
        profile.Children.Add(profileText);
        profile.MouseLeftButtonUp += (_, _) => EditProfile();
        railTop.Children.Add(profile);
        clock.FontSize = 19; clock.Foreground = B("#342A0C"); clock.Margin = new Thickness(0, 0, 0, 13);
        railTop.Children.Add(clock);
        DockPanel.SetDock(railTop, Dock.Top); railContent.Children.Add(railTop);

        var railFooter = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        railFooter.Children.Add(T("A  Seleccionar", 10, "#493A0D", FontWeights.SemiBold));
        railFooter.Children.Add(T("B  Volver", 10, "#493A0D", FontWeights.SemiBold));
        railFooter.Children.Add(T("X  Opciones", 10, "#493A0D", FontWeights.SemiBold));
        DockPanel.SetDock(railFooter, Dock.Bottom); railContent.Children.Add(railFooter);
        var nav = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        for (int n = 0; n < blades.Length; n++)
        {
            var tab = MakeBlade(blades[n], n);
            tab.Height = 45; tab.Margin = new Thickness(0, 2, 0, 2);
            tab.BorderThickness = new Thickness(1);
            bladeTabs[n] = tab;
            nav.Children.Add(tab);
        }
        var navScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav };
        DockPanel.SetDock(navScroll, Dock.Top); railContent.Children.Add(navScroll);
        goldRail.Children.Add(railContent);
        Grid.SetColumn(goldRail, 0); shell.Children.Add(goldRail);

        var content = new DockPanel { Background = G("#202326", "#101113"), Margin = new Thickness(0) };
        var header = new Grid { Height = 82, Margin = new Thickness(19, 13, 17, 7) };
        header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.FontSize = 25; title.FontWeight = FontWeights.SemiBold; title.Foreground = B("#FFFFFF");
        header.Children.Add(title);
        var liveMark = T("● LOCAL", 10, "#F3D45C", FontWeights.SemiBold);
        Grid.SetColumn(liveMark, 1); header.Children.Add(liveMark);
        subtitle.FontSize = 11; subtitle.Foreground = B("#A9B0B5"); subtitle.Margin = new Thickness(1, 0, 0, 0);
        Grid.SetRow(subtitle, 1); header.Children.Add(subtitle);
        DockPanel.SetDock(header, Dock.Top); content.Children.Add(header);
        var separator = new Border { Height = 1, Background = B("#414448"), Margin = new Thickness(18, 0, 18, 9) };
        DockPanel.SetDock(separator, Dock.Top); content.Children.Add(separator);

        var frame = new Border
        {
            Margin = new Thickness(13, 0, 13, 9), Padding = new Thickness(8),
            Background = G("#292C2F", "#1D1F21"), BorderBrush = B("#414448"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = rows }
        };
        content.Children.Add(frame);
        status.FontSize = 10; status.Foreground = B("#D5D5D5"); status.Margin = new Thickness(14, 0, 12, 10);
        DockPanel.SetDock(status, Dock.Bottom); content.Children.Add(status);
        var bottom = new Border { Height = 43, Background = B("#151719"), BorderBrush = B("#35383B"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(7, 0, 8, 0) };
        var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bottomRow.Children.Add(Hint("A", "#6ABF40", "Elegir"));
        bottomRow.Children.Add(Hint("B", "#C94942", "Atrás"));
        bottomRow.Children.Add(Hint("X", "#D6A83B", "Opciones"));
        bottomRow.Children.Add(T("  WIN + G", 9, "#92999E", FontWeights.SemiBold));
        bottom.Child = bottomRow;
        DockPanel.SetDock(bottom, Dock.Bottom); content.Children.Add(bottom);
        Grid.SetColumn(content, 1); shell.Children.Add(content);
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
        string[] accents = { "#67B53B", "#287DB2", "#8057A8", "#C94B42", "#D49A26", "#388F87" };
        var accent = accents[index % accents.Length];
        var label = T(blade.Name.ToUpperInvariant(), 13, "#FFFFFF", FontWeights.Bold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        var sub = T(blade.Subtitle, 9, "#E9E9E9");
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.TextAlignment = TextAlignment.Center;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 2, 4, 5) };
        stack.Children.Add(label);
        stack.Children.Add(sub);
        var marker = new Border { Height = 5, VerticalAlignment = VerticalAlignment.Bottom, Background = B(accent), Visibility = Visibility.Collapsed };
        var layers = new Grid();
        layers.Children.Add(stack); layers.Children.Add(marker);
        var tab = new Border
        {
            Margin = new Thickness(index == 0 ? 0 : 2, index == 0 ? 0 : 7, 2, 0),
            Background = G(index == 0 ? "#7CC84E" : accent, index == 0 ? "#347D22" : "#333333"),
            BorderBrush = B("#D9D9D9"), BorderThickness = new Thickness(1, 1, 1, 0),
            Child = layers, Cursor = Cursors.Hand
        };
        tab.MouseLeftButtonUp += (_, _) => SelectBlade(index, true);
        return tab;
    }

    void RefreshTheme()
    {
        DrawTabs();
        DrawRows();
        RefreshProfileHeader();
    }

    public void EditTheme()
    {
        var dialog = new Window
        {
            Title = "Tema del Guide",
            Width = 420, Height = 300, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Background = B("#EEEEEE")
        };
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(T("Personalización del Guide", 20, "#292929", FontWeights.SemiBold));
        layout.Children.Add(T("Color de énfasis y apariencia", 12, "#555555"));
        var modes = new ComboBox { Margin = new Thickness(0, 10, 0, 10), Padding = new Thickness(6), FontSize = 14 };
        modes.Items.Add("Clásico Xbox 360 (dorado)");
        modes.Items.Add("Igual al sistema (color de énfasis)");
        modes.Items.Add("Aleatorio al abrir");
        modes.Items.Add("Personalizado");
        modes.SelectedIndex = GuideTheme.Mode;
        layout.Children.Add(modes);
        var custom = new TextBox { Text = GuideTheme.CustomAccent, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(6), FontSize = 14 };
        custom.ToolTip = "Color hexadecimal, por ejemplo #FF6A00";
        layout.Children.Add(custom);
        layout.Children.Add(T("El modo sistema lee el color de énfasis de Windows. El fondo de pantalla no siempre proporciona una paleta fiable.", 11, "#555555"));
        var save = new Button { Content = "Guardar tema", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            if (modes.SelectedIndex == 3 && !System.Text.RegularExpressions.Regex.IsMatch(custom.Text.Trim(), "^#([0-9a-fA-F]{6})$"))
            {
                MessageBox.Show(dialog, "Escribe un color hexadecimal válido, por ejemplo #FF6A00.", "Tema", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            GuideTheme.SetMode(modes.SelectedIndex, custom.Text.Trim());
            RefreshTheme();
            dialog.Close();
        };
        layout.Children.Add(save);
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    void DrawTabs()
    {
        string[] accents = { "#67B53B", "#287DB2", "#8057A8", "#C94B42", "#D49A26", "#388F87" };
        for (int n = 0; n < blades.Length; n++)
        {
            bool selected = n == activeBlade;
            var tab = bladeTabs[n];
            string accent = accents[n % accents.Length];
            tab.Margin = new Thickness(0, 2, 0, 2);
            tab.Background = selected ? G("#536347", "#34442D") : G("#34383B", "#24272A");
            tab.BorderBrush = B(selected ? "#A5D66B" : "#484D51");
            if (tab.Child is Grid layers)
            {
                ((Border)layers.Children[1]).Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (layers.Children[0] is StackPanel stack)
                {
                    if (stack.Children[0] is TextBlock label)
                    {
                        label.Foreground = B(selected ? "#292929" : "#FFFFFF");
                        label.FontSize = selected ? 15 : 13;
                    }
                    if (stack.Children[1] is TextBlock sub)
                    {
                        sub.Foreground = B(selected ? "#5A5A5A" : "#E9E9E9");
                        sub.Text = selected ? blades[n].Subtitle : "";
                    }
                }
            }
        }
        title.Text = blades[activeBlade].Name;
        subtitle.Text = blades[activeBlade].Subtitle;
    }

    void DrawRows()
    {
        rows.Children.Clear();
        if (browsingFiles) { DrawBrowserRows(); return; }
        var entries = blades[activeBlade].Entries;
        if (entries.Length == 0) { rows.Children.Add(T("No hay elementos.", 14)); return; }
        activeRow = Math.Clamp(activeRow, 0, entries.Length - 1);
        for (int n = 0; n < entries.Length; n++)
        {
            int picked = n; bool selected = n == activeRow;
            var copy = new StackPanel { Margin = new Thickness(12, 4, 8, 4) };
            copy.Children.Add(T(entries[n].Name, 15, "#FFFFFF", selected ? FontWeights.SemiBold : FontWeights.Normal));
            copy.Children.Add(T(entries[n].Detail, 11, selected ? "#F2FFE9" : "#B5BDC2"));
            var stripe = new Border { Width = 4, Margin = new Thickness(0, 5, 0, 5), Background = B(selected ? "#D9F5C9" : "#626A70") };
            var dock = new DockPanel(); DockPanel.SetDock(stripe, Dock.Left); dock.Children.Add(stripe); dock.Children.Add(copy);
            var row = new Border
            {
                Height = 66, Margin = new Thickness(0, 0, 0, 5), Padding = new Thickness(5, 0, 5, 0),
                Background = selected ? G("#6ABF40", "#3B8E2B") : G("#363A3D", "#292C2F"),
                BorderBrush = B(selected ? "#A4E17F" : "#454A4E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Child = dock, Cursor = Cursors.Hand
            };
            row.MouseLeftButtonUp += (_, _) => { activeRow = picked; DrawRows(); ExecuteSelected(); };
            rows.Children.Add(row);
        }
    }

    public void OpenFileBrowser()
    {
        // Always start at the drive list, never at a stale path from a previous visit.
        browsingFiles = true;
        browserPath = null;
        browserItems.Clear();
        activeRow = 0;
        title.Text = "Explorador de juegos";
        status.Text = "Selecciona una unidad o carpeta · A/Enter abre · B/Esc vuelve";
        LoadBrowserItems();
        DrawRows();
    }

    void LoadBrowserItems()
    {
        browserItems.Clear();
        try
        {
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                foreach (var drive in DriveInfo.GetDrives())
                    if (drive.IsReady) browserItems.Add(new DirectoryInfo(drive.RootDirectory.FullName));
            }
            else
            {
                var dir = new DirectoryInfo(browserPath);
                if (dir.Parent != null) browserItems.Add(dir.Parent);
                foreach (var child in dir.EnumerateDirectories()) browserItems.Add(child);
                foreach (var file in dir.EnumerateFiles())
                    if (file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || file.Extension.Equals(".xex", StringComparison.OrdinalIgnoreCase)) browserItems.Add(file);
            }
        }
        catch (Exception ex) { status.Text = "No se pudo leer la carpeta: " + ex.Message; }
        activeRow = Math.Clamp(activeRow, 0, Math.Max(0, browserItems.Count - 1));
    }

    void DrawBrowserRows()
    {
        string location = string.IsNullOrWhiteSpace(browserPath) ? "Este equipo · unidades disponibles" : browserPath;
        title.Text = "Explorador de juegos";

        var toolbar = new WrapPanel { Margin = new Thickness(2, 0, 2, 10) };
        var back = new Button { Content = "← Carpeta anterior", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 6, 5), IsEnabled = !string.IsNullOrWhiteSpace(browserPath) };
        back.Click += (_, _) => GoBrowserParent();
        var root = new Button { Content = "↺ Inicio / unidades", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 6, 5) };
        root.Click += (_, _) => ResetBrowserPath();
        toolbar.Children.Add(back); toolbar.Children.Add(root);
        rows.Children.Add(toolbar);
        var pathLabel = T(location, 10, "#F3D45C", FontWeights.SemiBold);
        pathLabel.Margin = new Thickness(3, 0, 3, 9);
        pathLabel.TextWrapping = TextWrapping.Wrap;
        rows.Children.Add(pathLabel);

        if (browserItems.Count == 0)
        {
            rows.Children.Add(T("No hay unidades o archivos compatibles en esta ubicación.", 13, "#FFFFFF"));
            return;
        }
        for (int n = 0; n < browserItems.Count; n++)
        {
            int picked = n; bool selected = n == activeRow;
            var item = browserItems[n]; bool directory = item is DirectoryInfo;
            string name = item.Name;
            if (string.IsNullOrEmpty(name)) name = item.FullName;
            string detail = directory ? (string.IsNullOrWhiteSpace(browserPath) ? "Unidad de almacenamiento" : "Carpeta") : item.Extension.Equals(".xex", StringComparison.OrdinalIgnoreCase) ? "Ejecutable Xbox 360 · requiere entorno compatible" : "Aplicación de Windows · .exe";
            var copy = new StackPanel { Margin = new Thickness(10, 3, 7, 3) };
            copy.Children.Add(T((directory ? "📁  " : item.Extension.Equals(".xex", StringComparison.OrdinalIgnoreCase) ? "X  " : "▶  ") + name, 13, selected ? "#FFFFFF" : "#E2E2E2", selected ? FontWeights.SemiBold : FontWeights.Normal));
            copy.Children.Add(T(detail, 10, selected ? "#F8EAA6" : "#B8B8B8"));
            var stripe = new Border { Width = 3, Margin = new Thickness(0, 4, 0, 4), Background = B(selected ? "#F4C31D" : "#777777") };
            var dock = new DockPanel(); DockPanel.SetDock(stripe, Dock.Left); dock.Children.Add(stripe); dock.Children.Add(copy);
            var row = new Border { MinHeight = 51, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(4, 0, 4, 0), Background = selected ? G("#8A7626", "#5C4D19") : G("#424242", "#303030"), BorderBrush = B(selected ? "#F4C31D" : "#555555"), BorderThickness = new Thickness(1), Child = dock, Cursor = Cursors.Hand };
            row.MouseLeftButtonUp += (_, _) => { activeRow = picked; DrawRows(); ExecuteSelected(); };
            rows.Children.Add(row);
        }
    }

    void GoBrowserParent()
    {
        if (string.IsNullOrWhiteSpace(browserPath)) return;
        var parent = Directory.GetParent(browserPath);
        browserPath = parent?.FullName;
        activeRow = 0;
        LoadBrowserItems();
        DrawRows();
    }

    void ResetBrowserPath()
    {
        browserPath = null;
        activeRow = 0;
        LoadBrowserItems();
        DrawRows();
    }

    void ExecuteBrowserSelected()
    {
        if (activeRow < 0 || activeRow >= browserItems.Count) return;
        var item = browserItems[activeRow];
        if (item is DirectoryInfo directory)
        {
            browserPath = directory.FullName;
            activeRow = 0;
            LoadBrowserItems(); DrawRows();
            return;
        }
        if (item.Extension.Equals(".xex", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Los .xex no se ejecutan directamente en Windows; necesitas un entorno compatible.";
            return;
        }
        CloseAnimated();
        Dispatcher.BeginInvoke(new Action(() => Program.LaunchFromBrowser(item.FullName)), DispatcherPriority.Background);
    }

    void LeaveFileBrowser()
    {
        if (!browsingFiles) { CloseAnimated(); return; }
        if (!string.IsNullOrWhiteSpace(browserPath))
        {
            GoBrowserParent();
            return;
        }
        browsingFiles = false; activeRow = 0; DrawTabs(); DrawRows(); SetStatus();
    }

    void RegisterHotkeys()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        hotkey1 = RegisterHotKey(hwnd, Hotkey1, ModWin | ModNoRepeat, 0x47);
        hotkey2 = RegisterHotKey(hwnd, Hotkey2, ModControl | ModAlt | ModNoRepeat, 0x47);
        if (!hotkey2) status.Text = "Atajo alternativo ocupado; el hook global intenta capturar Win+G.";
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
        int count = browsingFiles ? browserItems.Count : blades[activeBlade].Entries.Length;
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
            case Key.Back: LeaveFileBrowser(); e.Handled = true; break;
        }
    }

    void ExecuteSelected()
    {
        if (animating) return;
        if (browsingFiles) { ExecuteBrowserSelected(); return; }
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
                if (!controllerProvider.TryGetState(user, out var state)) { previous[user] = 0; continue; }
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

    void RefreshProfileHeader()
    {
        profileName.Text = WindowsProfileService.GetDisplayName();
        profileInitials.Text = WindowsProfileService.GetInitials();
        string? path = WindowsProfileService.GetAvatarPath();
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                profileImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(path, UriKind.Absolute));
                profileImage.Visibility = Visibility.Visible;
                profileInitials.Visibility = Visibility.Collapsed;
                return;
            }
        }
        catch { }
        profileImage.Source = null;
        profileImage.Visibility = Visibility.Collapsed;
        profileInitials.Visibility = Visibility.Visible;
    }

    void EditProfile()
    {
        var editor = new Window
        {
            Title = "Perfil local",
            Width = 440,
            Height = 270,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = B("#E7E7E7")
        };
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(T("Tu perfil del Guide", 20, "#292929", FontWeights.SemiBold));
        layout.Children.Add(T("Nombre visible (inicialmente el de Windows)", 11, "#555555"));
        var nameBox = new TextBox { Text = WindowsProfileService.GetDisplayName(), Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(7), FontSize = 14 };
        layout.Children.Add(nameBox);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        var chooseAvatar = new Button { Content = "Cambiar imagen…", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        chooseAvatar.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Selecciona la imagen del perfil", Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos (*.*)|*.*", CheckFileExists = true };
            if (picker.ShowDialog(editor) == true)
            {
                try { WindowsProfileService.SetAvatar(picker.FileName); RefreshProfileHeader(); }
                catch (Exception ex) { MessageBox.Show(editor, ex.Message, "No se pudo cambiar la imagen", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        };
        var windowsAvatar = new Button { Content = "Usar imagen de Windows", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        windowsAvatar.Click += (_, _) => { WindowsProfileService.ResetAvatar(); RefreshProfileHeader(); };
        var windowsName = new Button { Content = "Nombre de Windows", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        windowsName.Click += (_, _) => { WindowsProfileService.ResetDisplayName(); nameBox.Text = WindowsProfileService.GetWindowsDisplayName(); RefreshProfileHeader(); };
        buttons.Children.Add(chooseAvatar); buttons.Children.Add(windowsAvatar); buttons.Children.Add(windowsName);
        layout.Children.Add(buttons);

        var save = new Button { Content = "Guardar nombre", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), MinWidth = 120 };
        save.Click += (_, _) =>
        {
            try { WindowsProfileService.SetDisplayName(nameBox.Text); RefreshProfileHeader(); editor.Close(); }
            catch (Exception ex) { MessageBox.Show(editor, ex.Message, "Perfil", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        layout.Children.Add(save);
        editor.Content = layout;
        editor.ShowDialog();
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

    internal static void LaunchFromBrowser(string target) => Launch(target);

    static void ShowLocalFeatureInfo(string feature)
    {
        MessageBox.Show(
            $"{feature} forma parte de la interfaz recreada, pero esta compilación funciona en modo local y no está conectada a Xbox LIVE. No se mostrarán amigos, mensajes ni estados inventados.",
            "Games for Windows LIVE 2026 · modo local",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

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
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Overlay? overlay = null;
        var blades = new[]
        {
            new Blade("Perfil", "Tu sesión local · sin Xbox LIVE", new[]
            {
                new Entry("Cuenta de Windows", "Abrir la configuración del perfil de Windows", () => Launch("ms-settings:yourinfo")),
                new Entry("Configuración", "Abrir configuración de Windows", () => Launch("ms-settings:")),
                new Entry("Diagnóstico de mandos", "Jugadores XInput y batería disponible", () => ControllerDiagnosticsWindow.Show(null)),
                new Entry("Tema del Guide", "Color clásico, aleatorio, sistema o personalizado", () => overlay?.EditTheme()),
                new Entry("Explorador de archivos", "Acceder a unidades y carpetas", () => Launch("explorer.exe")),
                new Entry("Bloquear equipo", "Volver a la pantalla de bloqueo", () => LockWorkStation())
            }),
            new Blade("Juegos", "Tiendas y ejecutables locales", new[]
            {
                new Entry("Steam", "Abrir biblioteca de Steam", () => Launch("steam://open/main")),
                new Entry("Epic Games Launcher", "Abrir Epic Games", () => Launch("com.epicgames.launcher://")),
                new Entry("GOG GALAXY", "Abrir GOG GALAXY", () => Launch("goggalaxy://")),
                new Entry("Explorar archivos de juegos", "Explorador integrado · unidades, carpetas y .exe", () => overlay?.OpenFileBrowser()),
                new Entry("Carpeta de programas", "Explorar Archivos de programa dentro del Guide", () => overlay?.OpenFileBrowser())
            }),
            new Blade("Social", "Amigos y comunicación · modo local", new[]
            {
                new Entry("Amigos", "La lista LIVE no está conectada", () => ShowLocalFeatureInfo("Amigos")),
                new Entry("Mensajes", "Mensajería Xbox LIVE no disponible sin backend", () => ShowLocalFeatureInfo("Mensajes")),
                new Entry("Grupo / chat de voz", "No hay servicio de grupo conectado", () => ShowLocalFeatureInfo("Grupo y chat de voz")),
                new Entry("Invitaciones", "Las invitaciones online no se reciben en modo local", () => ShowLocalFeatureInfo("Invitaciones"))
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
        overlay = new Overlay(blades);
        app.MainWindow = overlay;
        app.Run();
    }
}
