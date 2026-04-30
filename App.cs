using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using AccountingApp.Data;
using AccountingApp.Views;

namespace AccountingApp;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AccountingApp/Styles"))
        {
            Source = new Uri("avares://AccountingApp/Styles/AppStyles.axaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startupWindow = new DatabaseStartupWindow(database =>
            {
                var mainWindow = new MainWindow(database);
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
            });

            desktop.MainWindow = startupWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window CreateStartupErrorWindow(string message)
    {
        return new Window
        {
            Title = "会計ソフト - 設定エラー",
            Width = 720,
            Height = 360,
            Content = new Border
            {
                Padding = new Thickness(28),
                Background = Brushes.White,
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "設定が必要です",
                            FontSize = 24,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brush.Parse("#172033")
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = Brush.Parse("#4A5568")
                        },
                        new TextBlock
                        {
                            Text = "例: AccountingApp/appsettings.example.json を appsettings.json にコピーし、接続情報を設定してください。",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = Brush.Parse("#4A5568")
                        }
                    }
                }
            }
        };
    }
}
