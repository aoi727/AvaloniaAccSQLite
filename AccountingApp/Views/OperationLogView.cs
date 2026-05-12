using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class OperationLogView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _message = ViewHelpers.Body("操作履歴を読み込み中です。");
    private readonly TextBlock _count = ViewHelpers.Body("0");

    public OperationLogView(SqliteDatabase database, AppUser user, Action backToDashboard)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        Content = Build();
        _ = LoadAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.Click += (_, _) => _backToDashboard();

        var refreshButton = ViewHelpers.SecondaryButton("再読み込み");
        refreshButton.Width = 120;
        refreshButton.Click += async (_, _) => await LoadAsync();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,12,Auto"),
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        ViewHelpers.Heading(_user.CompanyName),
                        ViewHelpers.Body("操作履歴")
                    }
                },
                refreshButton,
                backButton
            }
        };
        Grid.SetColumn(refreshButton, 1);
        Grid.SetColumn(backButton, 3);

        var summary = ViewHelpers.Panel(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,24,*"),
            Children =
            {
                SummaryLabel("表示件数", 0),
                SummaryBox(_count, 1),
                _message
            }
        });
        Grid.SetColumn(_message, 3);

        var content = ViewHelpers.Panel(new ScrollViewer
        {
            Content = _rows
        });

        var layout = new Grid
        {
            Margin = new Thickness(28),
            RowDefinitions = new RowDefinitions("Auto,18,Auto,18,*"),
            Children =
            {
                header,
                summary,
                content
            }
        };
        Grid.SetRow(summary, 2);
        Grid.SetRow(content, 4);
        return layout;
    }

    private async Task LoadAsync()
    {
        try
        {
            var logs = await _database.GetOperationLogsAsync(_user.CompanyId, 200);
            _rows.Children.Clear();

            if (logs.Count == 0)
            {
                _count.Text = "0";
                _message.Text = "まだ表示できる操作履歴はありません。";
                _message.Foreground = Brush.Parse("#4A5568");
                _rows.Children.Add(ViewHelpers.Body("操作ログが記録されると、ここに新しい順で表示されます。"));
                return;
            }

            foreach (var log in logs)
            {
                _rows.Children.Add(LogRow(log));
            }

            _count.Text = logs.Count.ToString("N0");
            _message.Text = "直近 200 件の操作履歴を表示しています。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _rows.Children.Clear();
            _count.Text = "0";
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private static Control LogRow(OperationLogEntry log)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,150,150,160,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 6,
            Children =
            {
                Cell(log.OccurredAt.ToString("yyyy/MM/dd HH:mm:ss"), 0, 0, FontWeight.SemiBold),
                Cell(log.UserDisplayName ?? "system", 1, 0),
                Cell(ToOperationLabel(log.OperationType), 2, 0),
                Cell(BuildTargetText(log), 3, 0),
                Cell(log.Summary, 4, 0),
                Cell(string.IsNullOrWhiteSpace(log.MetadataJson) ? "" : log.MetadataJson, 0, 1, FontWeight.Normal, 5)
            }
        };

        return new Border
        {
            Background = Brush.Parse("#F8FAFC"),
            BorderBrush = Brush.Parse("#E2E8F0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Child = grid
        };
    }

    private static string BuildTargetText(OperationLogEntry log)
    {
        return string.IsNullOrWhiteSpace(log.TargetKey)
            ? log.TargetType
            : $"{log.TargetType}: {log.TargetKey}";
    }

    private static string ToOperationLabel(string operationType)
    {
        return operationType switch
        {
            "journal_create" => "仕訳登録",
            "journal_update" => "仕訳更新",
            "journal_delete" => "仕訳削除",
            "journal_template_save" => "定型保存",
            "journal_template_delete" => "定型削除",
            "budget_forecast_save" => "予算保存",
            "annual_close" => "年度締め",
            "annual_unlock" => "締め解除",
            "monthly_lock" => "月次ロック",
            "monthly_unlock" => "月次解除",
            _ => operationType
        };
    }

    private static TextBlock SummaryLabel(string text, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#243044")
        };
        Grid.SetColumn(label, column);
        return label;
    }

    private static Border SummaryBox(TextBlock value, int column)
    {
        value.HorizontalAlignment = HorizontalAlignment.Right;
        var box = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#8A8F96"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            MinHeight = 30,
            Child = value
        };
        Grid.SetColumn(box, column);
        return box;
    }

    private static TextBlock Cell(string text, int column, int row, FontWeight weight = default, int columnSpan = 1)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = weight == default ? FontWeight.Normal : weight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#243044")
        };
        Grid.SetColumn(block, column);
        Grid.SetRow(block, row);
        if (columnSpan > 1)
        {
            Grid.SetColumnSpan(block, columnSpan);
        }

        return block;
    }
}
