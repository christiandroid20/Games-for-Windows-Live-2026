using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

/// <summary>Reusable Guide building blocks: text, gloss buttons, selectable rows and controller balls.</summary>
internal static class GuideUi
{
    public static readonly FontFamily Sans = new("Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static Color K(string hex) => GuideTheme.Tint(hex);
    /// <summary>Gold chrome colour that follows the active theme.</summary>
    public static SolidColorBrush Themed(string hex) => new(K(hex));
    /// <summary>Colour that never changes with the theme (text, controller buttons, ring).</summary>
    public static SolidColorBrush Fixed(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    static LinearGradientBrush Gradient(Point start, Point end, (double Offset, string Hex)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (offset, hex) in stops) brush.GradientStops.Add(new GradientStop(K(hex), offset));
        return brush;
    }

    public static LinearGradientBrush Vertical(params (double Offset, string Hex)[] stops) => Gradient(new Point(0, 0), new Point(0, 1), stops);
    public static LinearGradientBrush Horizontal(params (double Offset, string Hex)[] stops) => Gradient(new Point(0, 0), new Point(1, 0), stops);

    public static TextBlock Text(string s, double size, string color = "#1B1709", FontWeight? weight = null) => new()
    {
        Text = s, FontSize = size, Foreground = Fixed(color), FontWeight = weight ?? FontWeights.Normal,
        FontFamily = Sans, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
    };

    public static TextBlock Glyph(string glyph, double size, string color = "#1B1709") => new()
    {
        Text = glyph, FontSize = size, Foreground = Fixed(color), FontFamily = Icons,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    static DropShadowEffect Glow() => new() { Color = Colors.White, BlurRadius = 14, ShadowDepth = 0, Opacity = .9 };

    static Brush SelectedFill() => Vertical((0, "#FFFBE0"), (0.5, "#FFEFA0"), (1, "#FFD95A"));

    /// <summary>Glossy gold button used by the Community counters and the media transport.</summary>
    public static Border Gloss(UIElement content, bool selected, bool enabled = true) => new()
    {
        Height = 52, CornerRadius = new CornerRadius(3), Child = content, Opacity = enabled ? 1 : .45, Cursor = Cursors.Hand,
        Background = selected ? SelectedFill() : Vertical((0, "#FCE27A"), (0.45, "#FBD64A"), (0.75, "#F2CC39"), (1, "#E3BE2C")),
        BorderBrush = selected ? Fixed("#FFFFFF") : Themed("#BDA32C"),
        BorderThickness = new Thickness(selected ? 2 : 1),
        Effect = selected ? Glow() : null
    };

    /// <summary>Flat list row with the pale separator of the original; selected rows glow.</summary>
    public static Border Row(UIElement content, double height, bool selected) => new()
    {
        Height = height, Padding = new Thickness(12, 0, 12, 0), Child = content, Cursor = Cursors.Hand,
        Background = selected ? SelectedFill() : Brushes.Transparent,
        BorderBrush = selected ? Fixed("#FFFFFF") : Themed("#EEC935"),
        BorderThickness = selected ? new Thickness(2) : new Thickness(0, 0, 0, 2),
        CornerRadius = new CornerRadius(selected ? 3 : 0),
        Effect = selected ? Glow() : null
    };

    /// <summary>Light card used for the gamer card, the Community box and the media box.</summary>
    public static Border Box(UIElement content, double height, bool selected = false) => new()
    {
        Height = height, Child = content,
        Background = Horizontal((0, "#F9CF3A"), (0.5, "#FDD847"), (1, "#E9C13C")),
        BorderBrush = selected ? Fixed("#FFFFFF") : Themed("#C4A639"),
        BorderThickness = new Thickness(selected ? 2 : 1), CornerRadius = new CornerRadius(3),
        Effect = selected ? Glow() : null
    };

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Glossy round controller button (A, B, X, Y).</summary>
    public static Border Ball(string letter, string hex)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(hex);
        var fill = new RadialGradientBrush { GradientOrigin = new Point(.35, .28), Center = new Point(.5, .5), RadiusX = .75, RadiusY = .75 };
        fill.GradientStops.Add(new GradientStop(Mix(baseColor, Colors.White, .55), 0));
        fill.GradientStops.Add(new GradientStop(baseColor, .55));
        fill.GradientStops.Add(new GradientStop(Mix(baseColor, Colors.Black, .35), 1));
        var label = Text(letter, 19, "#FFFFFF", FontWeights.Bold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = fill,
            BorderBrush = Fixed("#152B1A"), BorderThickness = new Thickness(1.5), Child = label
        };
    }

    /// <summary>Footer hint. The ball sits left of the text on the left column and right of it on the right column.</summary>
    public static StackPanel Hint(string letter, string hex, TextBlock label, bool ballFirst)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 38 };
        label.Foreground = Brushes.White; label.FontSize = 25; label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = ballFirst ? new Thickness(10, 0, 0, 0) : new Thickness(0, 0, 10, 0);
        var ball = Ball(letter, hex);
        if (ballFirst) { row.Children.Add(ball); row.Children.Add(label); }
        else { row.Children.Add(label); row.Children.Add(ball); }
        return row;
    }
}
