using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class ControllerDiagnosticsWindow
{
    public static void Show(Window? owner)
    {
        var provider = new XInputControllerProvider();
        var window = new Window
        {
            Title = "Diagnóstico de mandos",
            Width = 520,
            Height = 360,
            Owner = owner,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(235, 235, 235))
        };

        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock
        {
            Text = "Mandos XInput · hasta cuatro jugadores",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        int connected = 0;
        for (int player = 0; player < 4; player++)
        {
            if (!provider.TryGetState(player, out _))
            {
                layout.Children.Add(Line($"Jugador {player + 1}: sin mando XInput", "#777777"));
                continue;
            }

            connected++;
            var battery = provider.GetBattery(player);
            string connection = battery.Connection switch
            {
                ControllerConnectionKind.Wired => "USB / cableado",
                ControllerConnectionKind.Wireless => "inalámbrico",
                _ => "conexión desconocida"
            };
            string charge = battery.Connection == ControllerConnectionKind.Wired
                ? "batería no aplicable (cableado)"
                : battery.IsAvailable
                ? battery.Level switch
                {
                    ControllerBatteryLevel.Empty => "batería vacía",
                    ControllerBatteryLevel.Low => "batería baja",
                    ControllerBatteryLevel.Medium => "batería media",
                    ControllerBatteryLevel.Full => "batería llena",
                    _ => "batería no disponible"
                }
                : "batería no disponible";
            layout.Children.Add(Line($"Jugador {player + 1}: conectado · {connection} · {charge}", "#347D22"));
        }

        layout.Children.Add(new TextBlock
        {
            Text = $"Conectados por XInput: {connected}/4. Esta primera prueba aún no enumera todos los dispositivos HID físicos ni garantiza relacionar un mando virtual con su origen.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Margin = new Thickness(0, 14, 0, 12)
        });

        var close = new Button { Content = "Cerrar", Width = 100, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close();
        layout.Children.Add(close);
        window.Content = layout;
        window.ShowDialog();
    }

    private static TextBlock Line(string text, string color) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
        Margin = new Thickness(0, 4, 0, 4)
    };
}
