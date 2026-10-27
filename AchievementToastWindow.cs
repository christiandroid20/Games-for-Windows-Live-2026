using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

internal sealed class AchievementToastWindow : Window
{
    static AchievementToastWindow? current;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(6) };
    readonly TranslateTransform slide = new(0, 18);

    AchievementToastWindow(AchievementEvent achievement)
    {
        Title = "Logro desbloqueado";
        Width = 440;
        Height = 108;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Xbox 360-era toast, anchored to the bottom center as requested.
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Bottom - Height - 30;

        var panel = new Border
        {
            Padding = new Thickness(15, 12, 18, 12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(101, 123, 104)),
            Background = new LinearGradientBrush(
                Color.FromRgb(47, 54, 49), Color.FromRgb(19, 23, 21), 90),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = .7
            },
            RenderTransform = slide
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var orb = new Grid { Width = 54, Height = 54, VerticalAlignment = VerticalAlignment.Center };
        orb.Children.Add(new Ellipse
        {
            Fill = new LinearGradientBrush(Color.FromRgb(174, 220, 112), Color.FromRgb(44, 107, 43), 35),
            Stroke = new SolidColorBrush(Color.FromRgb(203, 235, 161)),
            StrokeThickness = 1.2
        });
        orb.Children.Add(new Ellipse
        {
            Margin = new Thickness(5),
            Fill = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90)
        });
        var glyph = new TextBlock
        {
            Text = "✦", FontSize = 28, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(245, 255, 222)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        orb.Children.Add(glyph);
        Grid.SetColumn(orb, 0);
        layout.Children.Add(orb);

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = "¡LOGRO DESBLOQUEADO!",
            FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 220, 174)),
            Margin = new Thickness(0, 0, 0, 5)
        });
        copy.Children.Add(new TextBlock
        {
            Text = achievement.Title, FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis
        });
        copy.Children.Add(new TextBlock
        {
            Text = achievement.Game, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 210, 203)),
            Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(copy, 1);
        layout.Children.Add(copy);

        var score = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        score.Children.Add(new TextBlock
        {
            Text = achievement.Points.ToString(),
            FontSize = 23, FontWeight = FontWeights.Light,
            Foreground = new SolidColorBrush(Color.FromRgb(206, 232, 188)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        score.Children.Add(new TextBlock
        {
            Text = "G", FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(206, 232, 188)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        Grid.SetColumn(score, 2);
        layout.Children.Add(score);

        panel.Child = layout;
        Content = panel;
        timer.Tick += (_, _) => FadeOutAndClose();
        Closed += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(current, this)) current = null;
        };
    }

    public static void ShowToast(AchievementEvent achievement)
    {
        current?.Close();
        current = new AchievementToastWindow(achievement);
        current.Show();
        current.Opacity = 0;
        current.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        current.slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        PlayAchievementSound();
        current.timer.Start();
    }

    static void PlayAchievementSound()
    {
        try
        {
            string soundPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "achievement-unlocked.wav");
            if (File.Exists(soundPath))
            {
                var player = new SoundPlayer(soundPath);
                player.Play();
            }
        }
        catch { /* The toast must still appear if audio is unavailable. */ }
    }

    void FadeOutAndClose()
    {
        timer.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, 12, TimeSpan.FromMilliseconds(260)));
    }
}
