using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

internal sealed record Entry(string Name, string Detail, Action Run, bool KeepOpen = false);
internal sealed record Blade(string Name, string Subtitle, Entry[] Entries, bool Player = false);

/// <summary>
/// The compact Xbox 360 Guide: gold panel on the left (<see cref="GuideChrome"/>), the other
/// tabs as dark strips on the right edge. A Player tab holds the gamer card, Community counters,
/// media transport and logo plate; other tabs are list pages; "Configuración personal" and the
/// integrated file browser are sub-views that B / Esc back out of.
/// </summary>
internal sealed class Overlay : Window
{
    const int Hotkey1 = 1, Hotkey2 = 2, WmHotkey = 0x312;
    const uint ModWin = 8, ModControl = 2, ModAlt = 1, ModNoRepeat = 0x4000;
    const ushort GuideButton = 0x0400;
    const int VisibleRows = 7, MaxBrowserItems = 2000;

    // Player page rows and how many selectable cells each has.
    static readonly int[] PlayerCols = { 1, 3, 1, 1, 6, 1 };
    static readonly byte[] MediaKeys = { 0xB3, 0xB1, 0xB2, 0xB0, 0, 0xAD }; // play, prev, stop, next, (shuffle), mute

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    enum View { Tab, Sub, Files }

    readonly IControllerProvider controller = new XInputControllerProvider();
    readonly AchievementBridge achievementBridge = new();
    readonly Blade[] blades;
    readonly GuideChrome chrome = new();
    readonly Canvas leftCanvas = new() { Width = GuideChrome.DesignWidth, Height = GuideChrome.DesignHeight };
    readonly Canvas rightCanvas = new() { Height = GuideChrome.DesignHeight };
    readonly StackPanel body = new();
    readonly TextBlock clock = GuideUi.Text("", 28, "#FFFFFF");
    readonly TextBlock tabLabel = GuideUi.Text("", 32);
    readonly TextBlock hintX = GuideUi.Text("Bloquear equipo", 25, "#FFFFFF");
    readonly TranslateTransform slideLeft = new(), slideRight = new();
    readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock gameTitle = GuideUi.Text("Sin juego detectado", 25, "#FFFFFF");
    readonly TextBlock gameElapsed = GuideUi.Text("SESIÓN 00:00:00", 18, "#D8D8D8");
    readonly TextBlock gameState = GuideUi.Text("ESPERANDO JUEGO", 16, "#BEBEBE");
    string? trackedProcess = null;
    DateTime gameSessionStarted;

    readonly DispatcherTimer padTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(55) };
    readonly ushort[] previous = new ushort[4];
    readonly List<FileSystemInfo> browserItems = new();

    HwndSource? source;
    IntPtr previousWindow;
    View view;
    Entry[]? subEntries;
    string subTitle = "";
    string? browserPath, notice;
    bool browserHasParent, hotkey1, hotkey2, padUnavailable, animating;
    int tab, row, col;
    string profileName = "", initials = "";
    ImageSource? avatar;

    public Overlay(Blade[] sourceBlades)
    {
        blades = sourceBlades;
        Title = "Games for Windows LIVE 2026";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        Topmost = true; ShowInTaskbar = false; WindowState = WindowState.Maximized;
        UseLayoutRounding = true; SnapsToDevicePixels = true;

        BuildLayout();
        achievementBridge.AchievementReceived += achievement => Dispatcher.BeginInvoke(new Action(() => AchievementToastWindow.ShowToast(achievement)));
        achievementBridge.Start();
        ShowLocalAchievement("welcome");
        PreviewKeyDown += KeyPressed;
        PreviewMouseWheel += (_, e) => { Move(0, e.Delta > 0 ? -1 : 1); e.Handled = true; };

        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        source?.AddHook(Hook);
        RegisterHotkeys();
        Closed += (_, _) => Cleanup();

        clockTimer.Tick += (_, _) => { clock.Text = DateTime.Now.ToString("h:mm tt"); if (GuideTheme.RefreshAccent()) RefreshTheme(); };
        gameTimer.Tick += (_, _) => UpdateGameSession();
        padTimer.Tick += (_, _) => PollPad();
        clockTimer.Start(); padTimer.Start(); gameTimer.Start();
        clock.Text = DateTime.Now.ToString("h:mm tt");
        RefreshProfileHeader();
        Refresh();
    }

    // ───────────────────────── layout ─────────────────────────

    static void Put(Canvas canvas, UIElement element, double x, double y, double w = double.NaN, double h = double.NaN)
    {
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y);
        if (element is FrameworkElement f)
        {
            if (!double.IsNaN(w)) f.Width = w;
            if (!double.IsNaN(h)) f.Height = h;
        }
        canvas.Children.Add(element);
    }

    void BuildLayout()
    {
        leftCanvas.Children.Add(chrome);

        clock.TextAlignment = TextAlignment.Right;
        Put(leftCanvas, clock, 400, 98, 312, 40);

        var bodyHost = new Grid { ClipToBounds = true };
        bodyHost.Children.Add(body);
        Put(leftCanvas, bodyHost, 154, 165, 555, 695);

        // The active tab's name runs top-to-bottom on the gold shoulder, as in the original.
        tabLabel.LayoutTransform = new RotateTransform(90);
        tabLabel.HorizontalAlignment = HorizontalAlignment.Center;
        tabLabel.TextTrimming = TextTrimming.None;
        var labelHost = new Grid();
        labelHost.Children.Add(tabLabel);
        Put(leftCanvas, labelHost, 728, 130, 84, 590);

        var left = new StackPanel();
        left.Children.Add(GuideUi.Hint("Y", "#E8C21A", GuideUi.Text("Cerrar Guide", 25), true));
        left.Children.Add(GuideUi.Hint("X", "#2C6FB0", hintX, true));
        Put(leftCanvas, left, 154, 902);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var back = GuideUi.Hint("B", "#C0392B", GuideUi.Text("Atrás", 25), false);
        var pick = GuideUi.Hint("A", "#3E9A2C", GuideUi.Text("Seleccionar", 25), false);
        back.HorizontalAlignment = HorizontalAlignment.Right; pick.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(back); right.Children.Add(pick);
        var rightHost = new Grid();
        rightHost.Children.Add(right);
        Put(leftCanvas, rightHost, 455, 904, 256);

        var leftBox = new Viewbox
        {
            Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch, Child = leftCanvas, RenderTransform = slideLeft
        };
        var rightBox = new Viewbox
        {
            Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch, Child = rightCanvas, RenderTransform = slideRight
        };
        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(leftBox); root.Children.Add(rightBox);
        root.MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, root)) CloseAnimated(); };
        Content = root;
    }

    // The inactive tabs recede as dark strips against the right screen edge.
    void BuildRing()
    {
        rightCanvas.Children.Clear();
        var others = Enumerable.Range(0, blades.Length).Where(i => i != tab).ToList();
        int n = others.Count;
        rightCanvas.Width = 140 + 80 * n;
        Put(rightCanvas, new Border { Background = GuideUi.Fixed("#343536"), BorderBrush = GuideUi.Fixed("#101010"), BorderThickness = new Thickness(2, 0, 0, 0) },
            80 * n, 0, 140, GuideChrome.DesignHeight);
        for (int k = n - 1; k >= 0; k--)
        {
            int index = others[k];
            var label = GuideUi.Text(blades[index].Name.ToLowerInvariant(), 32, "#111111");
            label.LayoutTransform = new RotateTransform(90);
            label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, 46, 0, 0); label.TextTrimming = TextTrimming.None;
            var band = new Border
            {
                Background = GuideUi.Horizontal((0, "#4A4A4A"), (1, "#383838")),
                BorderBrush = GuideUi.Fixed("#101010"), BorderThickness = new Thickness(2, 2, 0, 0),
                CornerRadius = new CornerRadius(18, 0, 0, 0), Child = label, Cursor = Cursors.Hand
            };
            band.MouseLeftButtonUp += (_, _) => SelectTab(index);
            double top = 150 + 36 * k;
            Put(rightCanvas, band, 80 * k, top, 92, GuideChrome.DesignHeight - top);
        }
    }

    // ───────────────────────── pages ─────────────────────────

    bool IsPlayerPage => view == View.Tab && blades[tab].Player;
    bool Sel(int r, int c = 0) => row == r && col == c;

    void Refresh()
    {
        body.Children.Clear();
        tabLabel.Text = blades[tab].Name;
        hintX.Text = view == View.Files ? "Unidades" : "Bloquear equipo";
        if (view == View.Files) BuildFiles();
        else if (IsPlayerPage) BuildPlayer();
        else BuildEntries();
        BuildRing();
    }

    void RefreshTheme()
    {
        chrome.InvalidateVisual();
        Refresh();
    }

    void BuildPlayer()
    {
        body.Children.Add(Card());
        body.Children.Add(Community());
        body.Children.Add(PlainRow("Chat privado", 2));
        body.Children.Add(PlainRow("Configuración personal", 3));
        body.Children.Add(GameSessionBox());
        body.Children.Add(MediaBox());
        body.Children.Add(Plate());
    }

    Border Card()
    {
        var picture = new Border { Width = 90, Height = 90, BorderBrush = GuideUi.Fixed("#8D7A2A"), BorderThickness = new Thickness(2), Background = GuideUi.Fixed("#5DAA37"), ClipToBounds = true };
        if (avatar != null) picture.Child = new Image { Source = avatar, Stretch = Stretch.UniformToFill };
        else
        {
            var letters = GuideUi.Text(initials, 40, "#FFFFFF", FontWeights.Bold);
            letters.HorizontalAlignment = HorizontalAlignment.Center;
            picture.Child = letters;
        }
        // Offline profile: nothing here pretends to come from Xbox LIVE.
        var lines = new Grid { Margin = new Thickness(15, 0, 0, 0) };
        lines.RowDefinitions.Add(new RowDefinition()); lines.RowDefinitions.Add(new RowDefinition());
        lines.ColumnDefinitions.Add(new ColumnDefinition()); lines.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddCell(lines, GuideUi.Text(profileName, 31), 0, 0);
        AddCell(lines, GuideUi.Text("0 G", 31), 1, 0);
        AddCell(lines, GuideUi.Text("Estado", 31), 0, 1);
        AddCell(lines, GuideUi.Text("Local", 31), 1, 1);
        var dock = new DockPanel();
        DockPanel.SetDock(picture, Dock.Left);
        dock.Children.Add(picture); dock.Children.Add(lines);
        var card = GuideUi.Box(dock, 121, Sel(0));
        card.Padding = new Thickness(15); card.Margin = new Thickness(0, 0, 0, 17); card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) => Pick(0, 0);
        return card;
    }

    static void AddCell(Grid grid, UIElement element, int column, int rowIndex)
    {
        Grid.SetColumn(element, column); Grid.SetRow(element, rowIndex);
        grid.Children.Add(element);
    }

    Border Community()
    {
        string[] glyphs = { "\uE715", "\uE716", "\uE7FC" }; // mail, people, game
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = GuideUi.Text("Comunidad", 30);
        title.Margin = new Thickness(0, 0, 0, 0);
        AddCell(grid, title, 0, 0);
        var buttons = new UniformGrid { Columns = 3, Rows = 1 };
        for (int c = 0; c < 3; c++)
        {
            int picked = c;
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition()); inner.ColumnDefinitions.Add(new ColumnDefinition());
            AddCell(inner, GuideUi.Glyph(glyphs[c], 24), 0, 0);
            var count = GuideUi.Text("—", 28); // no Xbox LIVE backend: counts are not available
            count.HorizontalAlignment = HorizontalAlignment.Center;
            AddCell(inner, count, 1, 0);
            var button = GuideUi.Gloss(inner, Sel(1, c));
            button.Margin = new Thickness(c == 0 ? 0 : 3, 0, c == 2 ? 0 : 3, 0);
            button.MouseLeftButtonUp += (_, _) => Pick(1, picked);
            buttons.Children.Add(button);
        }
        AddCell(grid, buttons, 0, 1);
        var box = GuideUi.Box(grid, 116);
        box.Padding = new Thickness(9, 0, 9, 9); box.Margin = new Thickness(0, 0, 0, 11);
        return box;
    }

    Border PlainRow(string text, int r)
    {
        var item = GuideUi.Row(GuideUi.Text(text, 30), 62, Sel(r));
        item.MouseLeftButtonUp += (_, _) => Pick(r, 0);
        return item;
    }

    Border GameSessionBox()
    {
        var content = new StackPanel();
        content.Children.Add(gameTitle);
        content.Children.Add(gameElapsed);
        content.Children.Add(gameState);
        var box = GuideUi.Box(content, 82);
        box.Padding = new Thickness(12, 7, 12, 7);
        box.Margin = new Thickness(0, 0, 0, 10);
        UpdateGameSession();
        return box;
    }

    void UpdateGameSession()
    {
        string? name = null;
        string? processKey = null;
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                    string exe = process.ProcessName;
                    string title = process.MainWindowTitle;
                    string[] ignored = { "BladesOverlay", "explorer", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "dwm", "taskhostw", "RuntimeBroker", "ApplicationFrameHost", "TextInputHost", "LockApp", "SystemSettings", "Taskmgr", "chrome", "msedge", "firefox", "opera", "brave" };
                    if (!ignored.Contains(exe, StringComparer.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(title) && pid != Environment.ProcessId)
                    {
                        name = title.Trim();
                        processKey = pid.ToString() + ":" + exe;
                    }
                }
            }
        }
        catch { }

        if (processKey != trackedProcess)
        {
            trackedProcess = processKey;
            gameSessionStarted = DateTime.Now;
            if (processKey != null) ShowLocalAchievement("game_seen");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            gameTitle.Text = "Sin juego detectado";
            gameElapsed.Text = "SESIÓN —";
            gameState.Text = "ESPERANDO JUEGO";
            return;
        }

        gameTitle.Text = name.Length > 42 ? name[..39] + "…" : name;
        TimeSpan elapsed = DateTime.Now - gameSessionStarted;
        gameElapsed.Text = $"SESIÓN {elapsed:hh\\:mm\\:ss}";
        gameState.Text = "VENTANA ACTIVA";
    }

    Border MediaBox()
    {
        string[] glyphs = { "\uE768", "\uE892", "\uE71A", "\uE893", "\uE8B1", "\uE767" }; // play, prev, stop, next, shuffle, volume
        var stack = new StackPanel();
        var buttons = new UniformGrid { Columns = 6, Rows = 1, Height = 44, Margin = new Thickness(0, 7, 0, 0) };
        for (int c = 0; c < 6; c++)
        {
            int picked = c;
            var button = GuideUi.Gloss(GuideUi.Glyph(glyphs[c], 22), Sel(4, c), enabled: c != 4);
            button.Margin = new Thickness(c == 0 ? 0 : 1.5, 0, c == 5 ? 0 : 1.5, 0);
            button.MouseLeftButtonUp += (_, _) => Pick(4, picked);
            buttons.Children.Add(button);
        }
        stack.Children.Add(buttons);
        var music = GuideUi.Row(GuideUi.Text("Seleccionar música", 30), 60, Sel(5));
        music.Margin = new Thickness(0, 6, 0, 0);
        music.MouseLeftButtonUp += (_, _) => Pick(5, 0);
        stack.Children.Add(music);
        var box = GuideUi.Box(stack, 137);
        box.Padding = new Thickness(9); box.Margin = new Thickness(0, 0, 0, 18);
        return box;
    }

    // Original artwork only: drop a brand.png next to the executable to show your own logo here.
    Border Plate()
    {
        UIElement content;
        string logo = System.IO.Path.Combine(AppContext.BaseDirectory, "brand.png");
        try
        {
            if (!File.Exists(logo)) throw new FileNotFoundException();
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(logo, UriKind.Absolute); bmp.EndInit(); bmp.Freeze();
            content = new Image { Source = bmp, Stretch = Stretch.Uniform, Margin = new Thickness(14) };
        }
        catch
        {
            var orb = new Ellipse
            {
                Width = 66, Height = 66, Stroke = GuideUi.Fixed("#9A9C92"), StrokeThickness = 1,
                Fill = new RadialGradientBrush(Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0x7A, 0x7C, 0x74)) { GradientOrigin = new Point(.35, .3) }
            };
            var name = GuideUi.Text("GAMES FOR WINDOWS", 38, "#8A8D82", FontWeights.Light);
            name.Margin = new Thickness(16, 0, 0, 0); name.TextTrimming = TextTrimming.None;
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(orb); line.Children.Add(name);
            content = line;
        }
        return new Border { Height = 113, Background = GuideUi.Fixed("#EEF2DD"), BorderBrush = GuideUi.Themed("#E4D483"), BorderThickness = new Thickness(1), Child = content };
    }

    StackPanel Heading(string title, string? subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(GuideUi.Text(title, 34, "#1B1709", FontWeights.SemiBold));
        string? line = notice ?? subtitle;
        if (!string.IsNullOrWhiteSpace(line)) panel.Children.Add(GuideUi.Text(line, 18, notice != null ? "#7A1F10" : "#5A4A10"));
        return panel;
    }

    int WindowTop(int count) => Math.Clamp(row - VisibleRows / 2, 0, Math.Max(0, count - VisibleRows));

    void AddCounter(int count, int top)
    {
        if (count <= VisibleRows) return;
        var text = GuideUi.Text($"{row + 1} / {count}" + (top > 0 ? "  ▲" : "") + (top + VisibleRows < count ? "  ▼" : ""), 18, "#5A4A10");
        text.HorizontalAlignment = HorizontalAlignment.Right;
        body.Children.Add(text);
    }

    UIElement ListItem(string name, string detail, bool selected, int index)
    {
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 5, 2, 5) };
        copy.Children.Add(GuideUi.Text(name, 28));
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var subtitle = GuideUi.Text(detail, 17, "#5A4A10");
            subtitle.Margin = new Thickness(0, 5, 0, 0);
            copy.Children.Add(subtitle);
        }
        var item = GuideUi.Row(copy, 84, selected);
        item.Margin = new Thickness(0, 0, 0, 5);
        item.MouseLeftButtonUp += (_, _) => Pick(index, 0);
        return item;
    }

    void BuildEntries()
    {
        var entries = view == View.Sub ? subEntries! : blades[tab].Entries;
        body.Children.Add(Heading(view == View.Sub ? subTitle : blades[tab].Name, view == View.Sub ? null : blades[tab].Subtitle));
        if (entries.Length == 0) { body.Children.Add(GuideUi.Text("No hay elementos.", 24)); return; }
        row = Math.Clamp(row, 0, entries.Length - 1); col = 0;
        int top = WindowTop(entries.Length);
        for (int n = top; n < Math.Min(entries.Length, top + VisibleRows); n++)
            body.Children.Add(ListItem(entries[n].Name, entries[n].Detail, n == row, n));
        AddCounter(entries.Length, top);
    }

    void BuildFiles()
    {
        body.Children.Add(Heading("Explorador de juegos", string.IsNullOrWhiteSpace(browserPath) ? "Este equipo · unidades disponibles" : browserPath));
        if (browserItems.Count == 0)
        {
            var empty = GuideUi.Text("No hay unidades o archivos compatibles en esta ubicación.", 24);
            empty.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(empty);
            return;
        }
        row = Math.Clamp(row, 0, browserItems.Count - 1); col = 0;
        int top = WindowTop(browserItems.Count);
        for (int n = top; n < Math.Min(browserItems.Count, top + VisibleRows); n++)
        {
            var item = browserItems[n];
            bool directory = item is DirectoryInfo;
            bool parent = browserHasParent && n == 0;
            string name = parent ? "↰  Carpeta superior" : string.IsNullOrEmpty(item.Name) ? item.FullName : item.Name;
            string detail = parent ? item.FullName
                : directory ? (string.IsNullOrWhiteSpace(browserPath) ? "Unidad de almacenamiento" : "Carpeta")
                : item.Extension.Equals(".xex", StringComparison.OrdinalIgnoreCase) ? "Ejecutable Xbox 360 · requiere entorno compatible"
                : "Aplicación de Windows · .exe";
            body.Children.Add(ListItem(name, detail, n == row, n));
        }
        AddCounter(browserItems.Count, top);
    }

    // ───────────────────────── file browser ─────────────────────────

    public void OpenFileBrowser()
    {
        // Always start at the drive list, never at a stale path from a previous visit.
        view = View.Files; browserPath = null; notice = null; row = 0; col = 0;
        LoadBrowserItems(null);
        Refresh();
    }

    // Loads a folder (null = drive list). On failure the previous list is kept and a notice is shown.
    bool LoadBrowserItems(string? path)
    {
        var found = new List<FileSystemInfo>();
        bool parent = false;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                foreach (var drive in DriveInfo.GetDrives())
                    if (drive.IsReady) found.Add(new DirectoryInfo(drive.RootDirectory.FullName));
            }
            else
            {
                var dir = new DirectoryInfo(path);
                if (dir.Parent != null) { found.Add(dir.Parent); parent = true; }
                var visible = FileAttributes.Hidden | FileAttributes.System;
                found.AddRange(dir.EnumerateDirectories().Where(d => (d.Attributes & visible) == 0)
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxBrowserItems));
                found.AddRange(dir.EnumerateFiles().Where(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".xex", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxBrowserItems));
            }
        }
        catch (Exception ex)
        {
            notice = "No se pudo abrir la carpeta: " + ex.Message;
            return false;
        }
        browserItems.Clear(); browserItems.AddRange(found);
        browserHasParent = parent; browserPath = string.IsNullOrWhiteSpace(path) ? null : path; notice = null;
        return true;
    }

    void EnterBrowserFolder(string? path)
    {
        row = 0;
        LoadBrowserItems(path);
        Refresh();
    }

    void ExecuteBrowserSelected()
    {
        if (row < 0 || row >= browserItems.Count) return;
        var item = browserItems[row];
        if (item is DirectoryInfo directory) { EnterBrowserFolder(directory.FullName); return; }
        if (item.Extension.Equals(".xex", StringComparison.OrdinalIgnoreCase))
        {
            notice = "Los .xex no se ejecutan directamente en Windows; necesitas un entorno compatible.";
            Refresh();
            return;
        }
        CloseAnimated();
        Dispatcher.BeginInvoke(new Action(() => Program.Launch(item.FullName)), DispatcherPriority.Background);
    }

    void LeaveFileBrowser()
    {
        if (!string.IsNullOrWhiteSpace(browserPath))
        {
            EnterBrowserFolder(Directory.GetParent(browserPath)?.FullName);
            return;
        }
        view = View.Tab; notice = null; row = 0; col = 0;
        Refresh();
    }

    // ───────────────────────── input ─────────────────────────

    void SelectTab(int next)
    {
        if (next < 0 || next >= blades.Length || (next == tab && view == View.Tab)) return;
        view = View.Tab; subEntries = null; notice = null;
        tab = next; row = 0; col = 0;
        Refresh();
        body.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void ChangeTab(int direction)
    {
        if (!IsVisible || animating || view != View.Tab) return;
        SelectTab((tab + direction + blades.Length) % blades.Length);
    }

    int RowCount() => view == View.Files ? browserItems.Count
        : IsPlayerPage ? PlayerCols.Length
        : (view == View.Sub ? subEntries!.Length : blades[tab].Entries.Length);

    int ColCount(int r) => IsPlayerPage && r >= 0 && r < PlayerCols.Length ? PlayerCols[r] : 1;

    void Move(int dx, int dy)
    {
        if (!IsVisible || animating) return;
        notice = null;
        if (dy != 0)
        {
            int count = RowCount();
            if (count == 0) return;
            row = (row + dy + count) % count;
            col = Math.Min(col, ColCount(row) - 1);
            Refresh();
            return;
        }
        if (ColCount(row) > 1) { col = Math.Clamp(col + dx, 0, ColCount(row) - 1); Refresh(); }
        else ChangeTab(dx);
    }

    void KeyPressed(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Move(-1, 0); break;
            case Key.Right: Move(1, 0); break;
            case Key.Up: Move(0, -1); break;
            case Key.Down: Move(0, 1); break;
            case Key.PageUp: ChangeTab(-1); break;
            case Key.PageDown: ChangeTab(1); break;
            case Key.Enter: ExecuteSelected(); break;
            case Key.Escape:
            case Key.Back: GoBack(); break;
            case Key.Home when view == View.Files: EnterBrowserFolder(null); break;
            default: return;
        }
        e.Handled = true;
    }

    void Pick(int r, int c)
    {
        if (animating) return;
        row = r; col = c;
        Refresh();
        ExecuteSelected();
    }

    void GoBack()
    {
        if (animating) return;
        if (view == View.Files) { LeaveFileBrowser(); return; }
        if (view == View.Sub) { view = View.Tab; subEntries = null; row = 3; col = 0; Refresh(); return; }
        CloseAnimated();
    }

    void ExecuteSelected()
    {
        if (animating) return;
        if (view == View.Files) { ExecuteBrowserSelected(); return; }
        if (IsPlayerPage) { PlayerAction(row, col); return; }
        var entries = view == View.Sub ? subEntries! : blades[tab].Entries;
        if (row < 0 || row >= entries.Length) return;
        var entry = entries[row];
        if (entry.KeepOpen) { entry.Run(); return; }
        CloseAnimated();
        Dispatcher.BeginInvoke(entry.Run, DispatcherPriority.Background);
    }

    static void SendMediaKey(byte vk)
    {
        if (vk == 0) return;
        keybd_event(vk, 0, 1, UIntPtr.Zero);  // key down (extended)
        keybd_event(vk, 0, 3, UIntPtr.Zero);  // key up (extended)
    }

    void PlayerAction(int r, int c)
    {
        switch (r)
        {
            case 0: EditProfile(); break;
            case 1: Program.ShowLocalFeatureInfo(this, new[] { "Mensajes", "Amigos", "Invitaciones" }[Math.Clamp(c, 0, 2)]); break;
            case 2: Program.ShowLocalFeatureInfo(this, "Chat privado"); break;
            case 3:
                subEntries = blades[tab].Entries; subTitle = "Configuración personal";
                view = View.Sub; row = 0; col = 0; Refresh();
                break;
            case 4: SendMediaKey(MediaKeys[Math.Clamp(c, 0, MediaKeys.Length - 1)]); break;
            case 5:
                CloseAnimated();
                Dispatcher.BeginInvoke(new Action(Program.OpenMusicFolder), DispatcherPriority.Background);
                break;
        }
    }

    void PollPad()
    {
        if (padUnavailable) return;
        try
        {
            for (int user = 0; user < 4; user++)
            {
                if (!controller.TryGetState(user, out var state)) { previous[user] = 0; continue; }
                ushort pressed = (ushort)(state.Buttons & ~previous[user]); previous[user] = state.Buttons;
                if ((pressed & GuideButton) != 0) { Toggle(); return; }
                if (!IsVisible || animating) continue;
                if ((pressed & 0x0100) != 0) ChangeTab(-1);            // LB
                else if ((pressed & 0x0200) != 0) ChangeTab(1);        // RB
                else if ((pressed & 0x0004) != 0) Move(-1, 0);         // D-pad left
                else if ((pressed & 0x0008) != 0) Move(1, 0);
                else if ((pressed & 0x0001) != 0) Move(0, -1);
                else if ((pressed & 0x0002) != 0) Move(0, 1);
                else if ((pressed & 0x1000) != 0) ExecuteSelected();   // A
                else if ((pressed & 0x2000) != 0) GoBack();            // B
                else if ((pressed & 0x8000) != 0) CloseAnimated();     // Y
                else if ((pressed & 0x4000) != 0)                      // X
                {
                    if (view == View.Files) EnterBrowserFolder(null);
                    else { CloseAnimated(); LockWorkStation(); }
                }
            }
        }
        catch (DllNotFoundException) { DisableGuide(); }
        catch (EntryPointNotFoundException) { DisableGuide(); }
        catch (BadImageFormatException) { DisableGuide(); }
    }

    void DisableGuide() { padUnavailable = true; }


    static void ShowLocalAchievement(string id)
    {
        AchievementEvent? achievement = LocalAchievementService.Unlock(id);
        if (achievement != null) AchievementToastWindow.ShowToast(achievement);
    }

    // ───────────────────────── window behaviour ─────────────────────────

    void RegisterHotkeys()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        hotkey1 = RegisterHotKey(hwnd, Hotkey1, ModWin | ModNoRepeat, 0x47);
        // Keep Ctrl+Alt+G as the keyboard fallback; XInput state alone does not\n        // expose the physical PlayStation PS/Guide button.\n        hotkey2 = RegisterHotKey(hwnd, Hotkey2, ModControl | ModAlt | ModNoRepeat, 0x47);
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
        ShowLocalAchievement("guide_open");
        animating = true;
        IntPtr self = new WindowInteropHelper(this).Handle;
        IntPtr foreground = GetForegroundWindow();
        previousWindow = foreground == self ? IntPtr.Zero : foreground;
        // Every opening starts clean: no stale sub-view, path, notice or selection.
        view = View.Tab; subEntries = null; notice = null; row = 0; col = 0;
        browserPath = null; browserItems.Clear();
        clock.Text = DateTime.Now.ToString("h:mm tt");
        Refresh();
        Opacity = 0; slideLeft.X = -70; slideRight.X = 70;
        Show(); Activate(); Focus();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        slideRight.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(70, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var slide = new DoubleAnimation(-70, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        slide.Completed += (_, _) => { animating = false; Focus(); };
        slideLeft.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    void CloseAnimated()
    {
        if (!IsVisible || animating) return;
        animating = true;
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120)));
        slideRight.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(slideRight.X, 70, TimeSpan.FromMilliseconds(140)));
        var slide = new DoubleAnimation(slideLeft.X, -70, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        slide.Completed += (_, _) =>
        {
            Hide();
            BeginAnimation(OpacityProperty, null); Opacity = 1;
            slideLeft.BeginAnimation(TranslateTransform.XProperty, null); slideLeft.X = 0;
            slideRight.BeginAnimation(TranslateTransform.XProperty, null); slideRight.X = 0;
            animating = false;
            if (previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow);
            previousWindow = IntPtr.Zero;
        };
        slideLeft.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    void Cleanup()
    {
        achievementBridge.Dispose();
        clockTimer.Stop(); padTimer.Stop(); gameTimer.Stop();
        if (source != null) source.RemoveHook(Hook);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hotkey1) UnregisterHotKey(hwnd, Hotkey1);
        if (hotkey2) UnregisterHotKey(hwnd, Hotkey2);
    }

    // ───────────────────────── profile and theme dialogs ─────────────────────────

    void RefreshProfileHeader()
    {
        profileName = WindowsProfileService.GetDisplayName();
        initials = WindowsProfileService.GetInitials();
        avatar = null;
        try
        {
            string? path = WindowsProfileService.GetAvatarPath();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path, UriKind.Absolute); bmp.EndInit(); bmp.Freeze();
                avatar = bmp;
            }
        }
        catch { avatar = null; }
    }

    static Window Dialog(Window owner, string title, double width, double height) => new()
    {
        Title = title, Width = width, Height = height, Owner = owner, Topmost = true,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = GuideUi.Fixed("#EEEEEE")
    };

    public void EditTheme()
    {
        var dialog = Dialog(this, "Tema del Guide", 420, 300);
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(GuideUi.Text("Personalización del Guide", 20, "#292929", FontWeights.SemiBold));
        layout.Children.Add(GuideUi.Text("Color de énfasis y apariencia", 12, "#555555"));
        var modes = new ComboBox { Margin = new Thickness(0, 10, 0, 10), Padding = new Thickness(6), FontSize = 14 };
        modes.Items.Add("Clásico Xbox 360 (dorado)");
        modes.Items.Add("Igual al sistema (color de énfasis)");
        modes.Items.Add("Aleatorio al abrir");
        modes.Items.Add("Personalizado");
        modes.Items.Add("Gris original metálico (Xbox 360)");
        modes.SelectedIndex = GuideTheme.Mode;
        layout.Children.Add(modes);
        var custom = new TextBox { Text = GuideTheme.CustomAccent, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(6), FontSize = 14, ToolTip = "Color hexadecimal, por ejemplo #FF6A00" };
        layout.Children.Add(custom);
        layout.Children.Add(GuideUi.Text("El modo gris recrea el acabado metálico neutro; el clásico conserva el dorado original.", 11, "#555555"));
        var save = new Button { Content = "Guardar tema", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            if (modes.SelectedIndex == 3 && !System.Text.RegularExpressions.Regex.IsMatch(custom.Text.Trim(), "^#([0-9a-fA-F]{6})$"))
            {
                MessageBox.Show(dialog, "Escribe un color hexadecimal válido, por ejemplo #FF6A00.", "Tema", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            GuideTheme.SetMode(modes.SelectedIndex, custom.Text.Trim());
            ShowLocalAchievement("theme");
            RefreshTheme();
            dialog.Close();
        };
        layout.Children.Add(save);
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    static string EnsureGamerPicturesGallery()
    {
        string gallery = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GamesForWindowsLIVE2026", "GamerPictures");
        Directory.CreateDirectory(gallery);

        // Downloads.zip is shipped beside the executable. Extract PNGs once so the
        // existing profile picker can use the images offline without requiring a manual install.
        string archive = System.IO.Path.Combine(AppContext.BaseDirectory, "Downloads.zip");
        if (File.Exists(archive) && !Directory.EnumerateFiles(gallery, "*.png").Any())
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;
                string fileName = System.IO.Path.GetFileName(entry.FullName);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                string target = System.IO.Path.Combine(gallery, fileName);
                using var source = entry.Open();
                using var destination = File.Create(target);
                source.CopyTo(destination);
            }
        }
        return gallery;
    }

    public void EditProfile()
    {
        var editor = Dialog(this, "Perfil local", 440, 270);
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(GuideUi.Text("Tu perfil del Guide", 20, "#292929", FontWeights.SemiBold));
        layout.Children.Add(GuideUi.Text("Nombre visible (inicialmente el de Windows)", 11, "#555555"));
        var nameBox = new TextBox { Text = WindowsProfileService.GetDisplayName(), Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(7), FontSize = 14 };
        layout.Children.Add(nameBox);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        var classicAvatar = new Button { Content = "Galería Xbox 360…", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        classicAvatar.Click += (_, _) =>
        {
            string gallery = EnsureGamerPicturesGallery();
            if (!Directory.Exists(gallery) || !Directory.EnumerateFiles(gallery, "*.png").Any())
            {
                MessageBox.Show(editor, "No se encontraron PNG en Downloads.zip ni en la galería local. Comprueba que el ZIP esté junto al ejecutable.", "Galería de perfiles", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new Window { Title = "Imágenes de jugador · Xbox 360", Width = 590, Height = 520, Owner = editor, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = GuideUi.Fixed("#EEEEEE"), MinWidth = 450, MinHeight = 360 };
            var root = new DockPanel { Margin = new Thickness(12) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);
            var apply = new Button { Content = "Usar selección", Padding = new Thickness(14, 6, 14, 6), IsEnabled = false };
            var cancel = new Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
            footer.Children.Add(apply); footer.Children.Add(cancel); root.Children.Add(footer);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var grid = new UniformGrid { Columns = 8 };
            string? selectedPath = null;
            foreach (string path in Directory.EnumerateFiles(gallery, "*.png").OrderBy(x => System.IO.Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path, UriKind.Absolute); image.DecodePixelWidth = 64; image.EndInit(); image.Freeze();
                var tile = new Border { Width = 58, Height = 58, Margin = new Thickness(3), Padding = new Thickness(2), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.White, Cursor = Cursors.Hand, Child = new Image { Source = image, Stretch = Stretch.Uniform } };
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    selectedPath = path;
                    foreach (Border item in grid.Children.OfType<Border>()) item.BorderBrush = ReferenceEquals(item, tile) ? GuideUi.Fixed("#D6B52A") : Brushes.Transparent;
                    apply.IsEnabled = true;
                };
                grid.Children.Add(tile);
            }
            scroll.Content = grid; root.Children.Add(scroll);
            apply.Click += (_, _) =>
            {
                if (selectedPath == null) return;
                try { WindowsProfileService.SetAvatar(selectedPath); RefreshProfileHeader(); Refresh(); picker.Close(); }
                catch (Exception ex) { MessageBox.Show(picker, ex.Message, "No se pudo guardar el avatar", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            picker.Content = root; picker.ShowDialog();
        };
        var chooseAvatar = new Button { Content = "Cambiar imagen…", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        chooseAvatar.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Selecciona la imagen del perfil", Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos (*.*)|*.*", CheckFileExists = true };
            if (picker.ShowDialog(editor) == true)
            {
                try { WindowsProfileService.SetAvatar(picker.FileName); RefreshProfileHeader(); Refresh(); }
                catch (Exception ex) { MessageBox.Show(editor, ex.Message, "No se pudo cambiar la imagen", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        };
        var windowsAvatar = new Button { Content = "Usar imagen de Windows", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        windowsAvatar.Click += (_, _) => { WindowsProfileService.ResetAvatar(); RefreshProfileHeader(); Refresh(); };
        var windowsName = new Button { Content = "Nombre de Windows", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6) };
        windowsName.Click += (_, _) => { WindowsProfileService.ResetDisplayName(); nameBox.Text = WindowsProfileService.GetWindowsDisplayName(); RefreshProfileHeader(); Refresh(); };
        buttons.Children.Add(classicAvatar); buttons.Children.Add(chooseAvatar); buttons.Children.Add(windowsAvatar); buttons.Children.Add(windowsName);
        layout.Children.Add(buttons);

        var save = new Button { Content = "Guardar nombre", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), MinWidth = 120 };
        save.Click += (_, _) =>
        {
            try { WindowsProfileService.SetDisplayName(nameBox.Text); ShowLocalAchievement("profile"); RefreshProfileHeader(); Refresh(); editor.Close(); }
            catch (Exception ex) { MessageBox.Show(editor, ex.Message, "Perfil", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        layout.Children.Add(save);
        editor.Content = layout;
        editor.ShowDialog();
    }
}
