using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class TaxSummaryView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly DatePicker _fromDate = new();
    private readonly DatePicker _toDate = new();
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly TextBlock _message = ViewHelpers.Body("消費税集計を読み込み中です。");
    private readonly TextBlock _grossTotal = ViewHelpers.Body("0");
    private readonly TextBlock _netTotal = ViewHelpers.Body("0");
    private readonly TextBlock _taxTotal = ViewHelpers.Body("0");
    private readonly TextBlock _creditableTotal = ViewHelpers.Body("0");
    private readonly TextBlock _nonCreditableTotal = ViewHelpers.Body("0");
    private bool _isInitializing;
    private bool _isAdjustingDateRange;

    public TaxSummaryView(SqliteDatabase database, AppUser user, Action backToDashboard)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        Content = Build();
        _fromDate.SelectedDateChanged += async (_, _) => await HandleFromDateChangedAsync();
        _toDate.SelectedDateChanged += async (_, _) => await HandleDateChangedAsync();
        _ = InitializeAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.Click += (_, _) => _backToDashboard();

        var refreshButton = ViewHelpers.SecondaryButton("再表示");
        refreshButton.Width = 100;
        refreshButton.Height = 32;
        refreshButton.VerticalAlignment = VerticalAlignment.Bottom;
        refreshButton.Click += async (_, _) => await LoadTaxSummaryAsync();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        ViewHelpers.Heading(_user.CompanyName),
                        ViewHelpers.Body("消費税集計")
                    }
                },
                backButton
            }
        };
        Grid.SetColumn(backButton, 1);

        var filterRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                WithMargin(Field("開始日", _fromDate), new Thickness(0, 0, 16, 12)),
                WithMargin(Field("終了日", _toDate), new Thickness(0, 0, 16, 12)),
                ButtonField(refreshButton)
            }
        };

        var controls = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                filterRow,
                SummaryPanel()
            }
        });

        var tableRows = new ScrollViewer { Content = _rows };
        Grid.SetRow(tableRows, 2);

        var table = ViewHelpers.Panel(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Children =
            {
                TableHeader(),
                _message,
                tableRows
            }
        });
        Grid.SetRow(_message, 1);

        var layout = new Grid
        {
            Margin = new Thickness(28),
            RowDefinitions = new RowDefinitions("Auto,18,Auto,18,*"),
            Children =
            {
                header,
                controls,
                table
            }
        };
        Grid.SetRow(controls, 2);
        Grid.SetRow(table, 4);
        return layout;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _isInitializing = true;
            var closingDay = await _database.GetCompanyClosingDayAsync(_user.CompanyId);
            var (fromDate, toDate) = GetLatestClosedRange(DateTime.Today, closingDay);
            _fromDate.SelectedDate = new DateTimeOffset(fromDate);
            _toDate.SelectedDate = new DateTimeOffset(toDate);
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            _isInitializing = false;
        }

        await LoadTaxSummaryAsync();
    }

    private async Task HandleDateChangedAsync()
    {
        if (_isInitializing || _isAdjustingDateRange)
        {
            return;
        }

        await LoadTaxSummaryAsync();
    }

    private async Task HandleFromDateChangedAsync()
    {
        if (_isInitializing || _isAdjustingDateRange)
        {
            return;
        }

        try
        {
            _isAdjustingDateRange = true;
            var fromDate = (_fromDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
            _toDate.SelectedDate = new DateTimeOffset(fromDate.AddMonths(1).AddDays(-1));
        }
        finally
        {
            _isAdjustingDateRange = false;
        }

        await LoadTaxSummaryAsync();
    }

    private async Task LoadTaxSummaryAsync()
    {
        try
        {
            var fromDate = (_fromDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
            var toDate = (_toDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
            var rows = await _database.GetTaxSummaryRowsAsync(_user.CompanyId, fromDate, toDate);

            _rows.Children.Clear();
            if (rows.Count == 0)
            {
                SetTotals([]);
                _message.Text = "指定期間に集計対象の消費税はありません。";
                _message.Foreground = Brush.Parse("#4A5568");
                return;
            }

            foreach (var row in rows)
            {
                _rows.Children.Add(TaxRow(row));
            }

            SetTotals(rows);
            _message.Text = $"{rows.Count:N0} 件の税区分を表示しています。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _rows.Children.Clear();
            SetTotals([]);
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private void SetTotals(IReadOnlyList<TaxSummaryRow> rows)
    {
        _grossTotal.Text = rows.Sum(x => x.GrossAmount).ToString("N0");
        _netTotal.Text = rows.Sum(x => x.NetAmount).ToString("N0");
        _taxTotal.Text = rows.Sum(x => x.TaxAmount).ToString("N0");
        _creditableTotal.Text = rows.Sum(x => x.CreditableTaxAmount).ToString("N0");
        _nonCreditableTotal.Text = rows.Sum(x => x.NonCreditableTaxAmount).ToString("N0");
    }

    private static (DateTime FromDate, DateTime ToDate) GetLatestClosedRange(DateTime today, int closingDay)
    {
        var normalizedClosingDay = Math.Clamp(closingDay, 1, 31);
        var thisMonthClosing = CreateClosingDate(today.Year, today.Month, normalizedClosingDay);
        var latestClosedEnd = today.Date > thisMonthClosing
            ? thisMonthClosing
            : CreateClosingDate(today.AddMonths(-1).Year, today.AddMonths(-1).Month, normalizedClosingDay);
        var previousClosing = CreateClosingDate(latestClosedEnd.AddMonths(-1).Year, latestClosedEnd.AddMonths(-1).Month, normalizedClosingDay);
        return (previousClosing.AddDays(1), latestClosedEnd);
    }

    private static DateTime CreateClosingDate(int year, int month, int closingDay)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Min(closingDay, lastDay));
    }

    private static Control Field(string label, Control input)
    {
        return new StackPanel
        {
            Spacing = 4,
            MinWidth = 220,
            Children =
            {
                ViewHelpers.Label(label),
                input
            }
        };
    }

    private static Control ButtonField(Control button)
    {
        return new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children =
            {
                new Border { Height = 28 },
                button
            }
        };
    }

    private static Control WithMargin(Control control, Thickness margin)
    {
        control.Margin = margin;
        return control;
    }

    private Control SummaryPanel()
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,18,Auto,120,18,Auto,120,18,Auto,120,18,Auto,120"),
            Children =
            {
                SummaryLabel("税込金額", 0),
                SummaryBox(_grossTotal, 1),
                SummaryLabel("税抜相当額", 3),
                SummaryBox(_netTotal, 4),
                SummaryLabel("消費税額", 6),
                SummaryBox(_taxTotal, 7),
                SummaryLabel("控除可", 9),
                SummaryBox(_creditableTotal, 10),
                SummaryLabel("控除不可", 12),
                SummaryBox(_nonCreditableTotal, 13)
            }
        };
    }

    private static Control TableHeader()
    {
        return new Border
        {
            Background = Brush.Parse("#E6E9ED"),
            BorderBrush = Brush.Parse("#8A8F96"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            Child = new Grid
            {
                ColumnDefinitions = TaxColumns(),
                Children =
                {
                    HeaderCell("税区分", 0),
                    HeaderCell("種類", 1),
                    HeaderCell("入力", 2),
                    HeaderCell("行数", 3, true),
                    HeaderCell("税込金額", 4, true),
                    HeaderCell("税抜相当額", 5, true),
                    HeaderCell("消費税額", 6, true),
                    HeaderCell("控除可", 7, true),
                    HeaderCell("控除不可", 8, true)
                }
            }
        };
    }

    private static Control TaxRow(TaxSummaryRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = TaxColumns(),
            Children =
            {
                Cell($"{row.TaxCode} {row.TaxName} {row.TaxRate:0.##}%", 0),
                Cell(ToTaxKindLabel(row.TaxKind), 1),
                Cell(ToTaxInputTypeLabel(row.TaxInputType), 2),
                AmountCell(row.LineCount, 3),
                AmountCell(row.GrossAmount, 4),
                AmountCell(row.NetAmount, 5),
                AmountCell(row.TaxAmount, 6),
                AmountCell(row.CreditableTaxAmount, 7),
                AmountCell(row.NonCreditableTaxAmount, 8)
            }
        };

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = grid
        };
    }

    private static ColumnDefinitions TaxColumns()
    {
        return new ColumnDefinitions("210,100,100,70,120,120,120,120,120");
    }

    private static TextBlock HeaderCell(string text, int column, bool rightAlign = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#172033"),
            HorizontalAlignment = rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static TextBlock Cell(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#243044"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static TextBlock AmountCell(decimal amount, int column)
    {
        var block = new TextBlock
        {
            Text = amount == 0 ? "" : amount.ToString("N0"),
            Foreground = Brush.Parse("#243044"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static TextBlock AmountCell(int amount, int column)
    {
        var block = new TextBlock
        {
            Text = amount.ToString("N0"),
            Foreground = Brush.Parse("#243044"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
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

    private static string ToTaxKindLabel(string taxKind)
    {
        return taxKind switch
        {
            "sales" => "売上",
            "purchase" => "仕入",
            "non_taxable" => "非課税",
            "exempt" => "免税",
            "out_of_scope" => "対象外",
            _ => taxKind
        };
    }

    private static string ToTaxInputTypeLabel(string taxInputType)
    {
        return taxInputType switch
        {
            "included" => "税込",
            "excluded" => "税抜",
            "none" => "なし",
            _ => taxInputType
        };
    }
}
