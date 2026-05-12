using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ReligiousReportApp.Views;

public static class ViewHelpers
{
    public static TextBlock Heading(string text, double size = 24)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#172033"),
            TextWrapping = TextWrapping.Wrap
        };
    }

    public static TextBlock Body(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = Brush.Parse("#4A5568"),
            TextWrapping = TextWrapping.Wrap
        };
    }

    public static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#243044")
        };
    }

    public static Button PrimaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = Brush.Parse("#315E93"),
            Foreground = Brushes.White,
            Padding = new Thickness(14, 7),
            CornerRadius = new CornerRadius(6)
        };
    }

    public static Button SecondaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = Brushes.White,
            Foreground = Brush.Parse("#243044"),
            BorderBrush = Brush.Parse("#AAB4C2"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 7),
            CornerRadius = new CornerRadius(6)
        };
    }

    public static Border Panel(Control child)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = child
        };
    }
}
