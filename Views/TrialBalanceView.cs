using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class TrialBalanceView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly DatePicker _fromDate = new();
    private readonly DatePicker _toDate = new();
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly TextBlock _message = ViewHelpers.Body("試算表を読み込み中です。");
    private readonly TextBlock _previousBalanceTotal = ViewHelpers.Body("0");
    private readonly TextBlock _debitTotal = ViewHelpers.Body("0");
    private readonly TextBlock _creditTotal = ViewHelpers.Body("0");
    private readonly TextBlock _endingBalanceTotal = ViewHelpers.Body("0");
    private bool _isInitializing;
    private bool _isAdjustingDateRange;

    public TrialBalanceView(SqliteDatabase database, AppUser user, Action backToDashboard)
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
        refreshButton.Click += async (_, _) => await LoadTrialBalanceAsync();

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
                        ViewHelpers.Body("試算表")
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

        await LoadTrialBalanceAsync();
    }

    private async Task HandleDateChangedAsync()
    {
        if (_isInitializing || _isAdjustingDateRange)
        {
            return;
        }

        await LoadTrialBalanceAsync();
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

        await LoadTrialBalanceAsync();
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
        var panel = new StackPanel
        {
            Spacing = 4,
            MinWidth = 220,
            Children =
            {
                ViewHelpers.Label(label),
                input
            }
        };
        return panel;
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
            ColumnDefinitions = new ColumnDefinitions("Auto,110,16,Auto,110,16,Auto,110,16,Auto,110"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SummaryLabel("前月残", 0),
                SummaryBox(_previousBalanceTotal, 1),
                SummaryLabel("借方", 3),
                SummaryBox(_debitTotal, 4),
                SummaryLabel("貸方", 6),
                SummaryBox(_creditTotal, 7),
                SummaryLabel("残高", 9),
                SummaryBox(_endingBalanceTotal, 10)
            }
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

    private static Control TableHeader()
    {
        var header = new Border
        {
            Background = Brush.Parse("#E6E9ED"),
            BorderBrush = Brush.Parse("#8A8F96"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            Child = new Grid
            {
                ColumnDefinitions = TableColumns(),
                Children =
                {
                    HeaderCell("勘定科目", 0),
                    HeaderCell("前月残", 1),
                    HeaderCell("借方", 2),
                    HeaderCell("貸方", 3),
                    HeaderCell("残高", 4)
                }
            }
        };
        Grid.SetRow(header, 0);
        return header;
    }

    private static ColumnDefinitions TableColumns()
    {
        return new ColumnDefinitions("*,130,130,130,130");
    }

    private static TextBlock HeaderCell(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#172033"),
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private async Task LoadTrialBalanceAsync()
    {
        var fromDate = (_fromDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
        var toDate = (_toDate.SelectedDate?.DateTime ?? DateTime.Today).Date;

        try
        {
            var rows = await _database.GetTrialBalanceRowsAsync(_user.CompanyId, fromDate, toDate);
            _rows.Children.Clear();

            foreach (var group in rows.GroupBy(x => AccountClassificationCatalog.ResolveProfile(x.AccountCode, x.AccountName, x.AccountType, x.BalanceSide).ClassificationName))
            {
                var profile = AccountClassificationCatalog.ResolveProfile(
                    group.First().AccountCode,
                    group.First().AccountName,
                    group.First().AccountType,
                    group.First().BalanceSide);
                _rows.Children.Add(CategoryHeaderRow(profile));

                foreach (var row in group)
                {
                    _rows.Children.Add(TableRow(row));
                }

                _rows.Children.Add(CategorySubtotalRow(profile.ClassificationName, group));
            }

            _previousBalanceTotal.Text = FormatTrialBalanceAmount(rows.Sum(GetReportedPreviousBalance));
            _debitTotal.Text = rows.Sum(x => x.DebitAmount).ToString("N0");
            _creditTotal.Text = rows.Sum(x => x.CreditAmount).ToString("N0");
            _endingBalanceTotal.Text = FormatTrialBalanceAmount(rows.Sum(GetReportedEndingBalance));
            _message.Text = $"{fromDate:yyyy/MM/dd} から {toDate:yyyy/MM/dd} までの試算表を表示しています。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private static Control TableRow(TrialBalanceRow row)
    {
        var profile = AccountClassificationCatalog.ResolveProfile(row.AccountCode, row.AccountName, row.AccountType, row.BalanceSide);
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = TableColumns(),
                Children =
                {
                    Cell($"{row.AccountCode} {row.AccountName}", 0, FontWeight.SemiBold),
                    AmountCell(GetReportedPreviousBalance(row), 1, false, profile.IsContraAccount),
                    AmountCell(row.DebitAmount, 2),
                    AmountCell(row.CreditAmount, 3),
                    AmountCell(GetReportedEndingBalance(row), 4, false, profile.IsContraAccount)
                }
            }
        };
    }

    private static Control CategoryHeaderRow(AccountReportProfile profile)
    {
        return new Border
        {
            Background = Brush.Parse("#EEF4FB"),
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 9),
            Child = new TextBlock
            {
                Text = $"{profile.ClassificationName}  [{GetStatementLabel(profile)}]",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#172033")
            }
        };
    }

    private static string GetStatementLabel(AccountReportProfile profile)
    {
        return profile.StatementKind switch
        {
            FinancialStatementKind.BalanceSheet => profile.StatementSection,
            FinancialStatementKind.IncomeStatement => profile.StatementSection,
            _ => "共通"
        };
    }

    private static Control CategorySubtotalRow(string classificationName, IEnumerable<TrialBalanceRow> rows)
    {
        var rowList = rows.ToList();
        return new Border
        {
            Background = Brush.Parse("#F8FAFC"),
            BorderBrush = Brush.Parse("#CBD5E1"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = TableColumns(),
                Children =
                {
                    Cell($"{classificationName} 小計", 0, FontWeight.SemiBold),
                    AmountCell(rowList.Sum(GetReportedPreviousBalance), 1, true),
                    AmountCell(rowList.Sum(x => x.DebitAmount), 2, true),
                    AmountCell(rowList.Sum(x => x.CreditAmount), 3, true),
                    AmountCell(rowList.Sum(GetReportedEndingBalance), 4, true)
                }
            }
        };
    }

    private static decimal GetReportedPreviousBalance(TrialBalanceRow row)
    {
        var profile = AccountClassificationCatalog.ResolveProfile(row.AccountCode, row.AccountName, row.AccountType, row.BalanceSide);
        return AccountClassificationCatalog.NormalizeBalanceForReports(row.PreviousBalance, profile.IsContraAccount);
    }

    private static decimal GetReportedEndingBalance(TrialBalanceRow row)
    {
        var profile = AccountClassificationCatalog.ResolveProfile(row.AccountCode, row.AccountName, row.AccountType, row.BalanceSide);
        return AccountClassificationCatalog.NormalizeBalanceForReports(row.EndingBalance, profile.IsContraAccount);
    }

    private static Control Cell(string text, int column, FontWeight weight = default)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = weight == default ? FontWeight.Normal : weight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#243044"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static Control AmountCell(decimal amount, int column, bool isEmphasized = false, bool useParenthesesForNegative = false)
    {
        var block = new TextBlock
        {
            Text = useParenthesesForNegative ? FormatTrialBalanceAmount(amount) : amount.ToString("N0"),
            Foreground = isEmphasized ? Brush.Parse("#172033") : Brush.Parse("#243044"),
            FontWeight = isEmphasized ? FontWeight.SemiBold : FontWeight.Normal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static string FormatTrialBalanceAmount(decimal amount)
    {
        return amount < 0
            ? $"({Math.Abs(amount).ToString("N0")})"
            : amount.ToString("N0");
    }
}
