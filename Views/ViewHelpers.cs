using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

internal static class ViewHelpers
{
    public static TextBlock Heading(string text, double size = 26)
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
            Foreground = Brush.Parse("#4A5568"),
            TextWrapping = TextWrapping.Wrap
        };
    }

    public static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 10, 0, 4),
            Foreground = Brush.Parse("#2D3748"),
            FontWeight = FontWeight.Medium
        };
    }

    public static Border Panel(Control content)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Child = content
        };
    }

    public static Button PrimaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = Brush.Parse("#1E6B52"),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    public static Button SecondaryButton(string text)
    {
        return new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }
}
