using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

internal sealed class AchievementToastWindow : Window
{
    static AchievementToastWindow? current;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(6) };

    AchievementToastWindow(AchievementEvent achievement)
    {
        Title = "Logro desbloqueado";
        Width = 420; Height = 112;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - Width - 28;
        Top = SystemParameters.WorkArea.Bottom - Height - 34;

        var root = new Border
        {
            CornerRadius = new CornerRadius(0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 190, 73)),
            BorderThickness = new Thickness(1.5),
            Background = new LinearGradientBrush(Color.FromRgb(47, 49, 49), Color.FromRgb(17, 18, 18), 90),
            Padding = new Thickness(16)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromRgb(224, 197, 91)),
            Background = new LinearGradientBrush(Color.FromRgb(240, 224, 158), Color.FromRgb(127, 105, 37), 45),
            Child = new TextBlock { Text = "G", FontSize = 32, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(45, 45, 39)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center }
        };
        Grid.SetColumn(badge, 0); layout.Children.Add(badge);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "LOGRO DESBLOQUEADO", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(195, 195, 187)), Margin = new Thickness(0, 0, 0, 2) });
        text.Children.Add(new TextBlock { Text = achievement.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = achievement.Game + (achievement.Points > 0 ? "    " + achievement.Points + " G" : ""), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(202, 202, 194)), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = achievement.Description, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 214)), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1); layout.Children.Add(text);
        root.Child = layout; Content = root;
        timer.Tick += (_, _) => Close();
        Closed += (_, _) => { timer.Stop(); if (ReferenceEquals(current, this)) current = null; };
    }

    public static void ShowToast(AchievementEvent achievement)
    {
        current?.Close();
        current = new AchievementToastWindow(achievement);
        current.Show();
        current.Opacity = 0;
        current.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        current.timer.Start();
    }
}
