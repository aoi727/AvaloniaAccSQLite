using System.Globalization;
using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class BudgetForecastView : UserControl
{
    private sealed record BudgetRowControls(
        DateTime MonthStart,
        TextBox SalesBudget,
        TextBox ExpenseBudget,
        TextBox ExpectedCashIn,
        TextBox ExpectedCashOut,
        TextBox Note);

    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly DatePicker _fiscalYearStart = new();
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly TextBlock _budgetProfit = ViewHelpers.Body("0");
    private readonly TextBlock _actualProfit = ViewHelpers.Body("0");
    private readonly TextBlock _landingProfit = ViewHelpers.Body("0");
    private readonly TextBlock _openingCash = ViewHelpers.Body("0");
    private readonly TextBlock _projectedCash = ViewHelpers.Body("0");
    private readonly TextBlock _message = ViewHelpers.Body("予算実績 / 資金繰り見込を読み込み中です。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("保存");
    private readonly List<BudgetRowControls> _rowControls = [];
    private DateTime _companyFiscalYearTemplate;
    private bool _isInitializing;

    public BudgetForecastView(SqliteDatabase database, AppUser user, Action backToDashboard)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        Content = Build();
        _fiscalYearStart.SelectedDateChanged += async (_, _) => await FiscalYearChangedAsync();
        _saveButton.Click += async (_, _) => await SaveAsync();
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
        refreshButton.Click += async (_, _) => await LoadAsync();

        var currentYearButton = ViewHelpers.SecondaryButton("当年度");
        currentYearButton.Width = 90;
        currentYearButton.Height = 32;
        currentYearButton.Click += async (_, _) =>
        {
            _fiscalYearStart.SelectedDate = new DateTimeOffset(GetFiscalYearStartFor(DateTime.Today));
            await LoadAsync();
        };

        var previousYearButton = ViewHelpers.SecondaryButton("前年度");
        previousYearButton.Width = 90;
        previousYearButton.Height = 32;
        previousYearButton.Click += async (_, _) =>
        {
            var current = (_fiscalYearStart.SelectedDate?.DateTime ?? GetFiscalYearStartFor(DateTime.Today)).Date;
            _fiscalYearStart.SelectedDate = new DateTimeOffset(current.AddYears(-1));
            await LoadAsync();
        };

        var nextYearButton = ViewHelpers.SecondaryButton("翌年度");
        nextYearButton.Width = 90;
        nextYearButton.Height = 32;
        nextYearButton.Click += async (_, _) =>
        {
            var current = (_fiscalYearStart.SelectedDate?.DateTime ?? GetFiscalYearStartFor(DateTime.Today)).Date;
            _fiscalYearStart.SelectedDate = new DateTimeOffset(current.AddYears(1));
            await LoadAsync();
        };

        _saveButton.Width = 100;
        _saveButton.Height = 32;

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
                        ViewHelpers.Body("予算実績 / 資金繰り見込")
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
                WithMargin(Field("年度開始日", _fiscalYearStart, 220), new Thickness(0, 0, 16, 12)),
                ButtonField(previousYearButton),
                ButtonField(currentYearButton),
                ButtonField(nextYearButton),
                ButtonField(refreshButton),
                ButtonField(_saveButton)
            }
        };

        var summaryPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,130,16,Auto,130,16,Auto,130,16,Auto,130,16,Auto,130"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SummaryLabel("予算利益", 0),
                SummaryBox(_budgetProfit, 1),
                SummaryLabel("実績利益", 3),
                SummaryBox(_actualProfit, 4),
                SummaryLabel("着地見込", 6),
                SummaryBox(_landingProfit, 7),
                SummaryLabel("期首現預金", 9),
                SummaryBox(_openingCash, 10),
                SummaryLabel("期末資金見込", 12),
                SummaryBox(_projectedCash, 13)
            }
        };

        var controls = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                filterRow,
                summaryPanel,
                _message
            }
        });

        var table = ViewHelpers.Panel(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                TableHeader(),
                new ScrollViewer { Content = _rows }
            }
        });
        Grid.SetRow(((Grid)table.Child!).Children[1], 1);

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
            var settings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            _companyFiscalYearTemplate = settings.FiscalYearStart.Date;
            _fiscalYearStart.SelectedDate = new DateTimeOffset(GetFiscalYearStartFor(DateTime.Today));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _isInitializing = false;
        }

        await LoadAsync();
    }

    private async Task FiscalYearChangedAsync()
    {
        if (_isInitializing)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var fiscalStart = (_fiscalYearStart.SelectedDate?.DateTime ?? GetFiscalYearStartFor(DateTime.Today)).Date;
        try
        {
            var summary = await _database.GetBudgetForecastSummaryAsync(_user.CompanyId, fiscalStart);
            RenderSummary(summary);
            RenderRows(summary.Rows);
            _message.Text = $"{summary.FiscalYearStart:yyyy/MM/dd} から {summary.FiscalYearEnd:yyyy/MM/dd} までを表示しています。実績確定: {summary.ActualThrough:yyyy/MM/dd}";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _rows.Children.Clear();
            _rowControls.Clear();
            SetError(ex.Message);
        }
    }

    private void RenderSummary(BudgetForecastSummary summary)
    {
        _budgetProfit.Text = FormatAmount(summary.BudgetProfitTotal);
        _actualProfit.Text = FormatAmount(summary.ActualProfitToDate);
        _landingProfit.Text = FormatAmount(summary.LandingProfitTotal);
        _openingCash.Text = FormatAmount(summary.OpeningCashBalance);
        _projectedCash.Text = FormatAmount(summary.ProjectedEndingCash);
        _projectedCash.Foreground = summary.ProjectedEndingCash < 0 ? Brush.Parse("#B42318") : Brush.Parse("#243044");
    }

    private void RenderRows(IReadOnlyList<BudgetForecastMonthRow> rows)
    {
        _rows.Children.Clear();
        _rowControls.Clear();

        if (rows.Count == 0)
        {
            _rows.Children.Add(ViewHelpers.Body("表示するデータがありません。"));
            return;
        }

        foreach (var row in rows)
        {
            _rows.Children.Add(BudgetRow(row));
        }
    }

    private Control BudgetRow(BudgetForecastMonthRow row)
    {
        var salesBudget = AmountInput(row.SalesBudget);
        var expenseBudget = AmountInput(row.ExpenseBudget);
        var expectedCashIn = AmountInput(row.ExpectedCashIn);
        var expectedCashOut = AmountInput(row.ExpectedCashOut);
        var note = new TextBox
        {
            Text = row.Note ?? "",
            MinWidth = 160
        };

        _rowControls.Add(new BudgetRowControls(row.MonthStart, salesBudget, expenseBudget, expectedCashIn, expectedCashOut, note));

        var status = row.IsActualClosed ? "実績" : "見込";
        var background = row.IsActualClosed ? Brushes.White : Brush.Parse("#F8FAFC");
        var profitVariance = row.ProfitVariance;
        var cashBrush = row.CashEndingBalance < 0 ? Brush.Parse("#B42318") : Brush.Parse("#243044");

        return new Border
        {
            Background = background,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6),
            Child = new Grid
            {
                ColumnDefinitions = TableColumns(),
                Children =
                {
                    Cell(row.MonthStart.ToString("yyyy/MM"), 0, FontWeight.SemiBold),
                    Cell(status, 1),
                    AmountCell(row.ActualSales, 2),
                    InputCell(salesBudget, 3),
                    AmountCell(row.ActualExpenses, 4),
                    InputCell(expenseBudget, 5),
                    AmountCell(row.ActualProfit, 6),
                    AmountCell(row.BudgetProfit, 7),
                    AmountCell(profitVariance, 8, profitVariance < 0 ? Brush.Parse("#B42318") : Brush.Parse("#1E6B52")),
                    AmountCell(row.LandingProfit, 9),
                    InputCell(expectedCashIn, 10),
                    InputCell(expectedCashOut, 11),
                    AmountCell(row.CashMovement, 12),
                    AmountCell(row.CashEndingBalance, 13, cashBrush),
                    InputCell(note, 14)
                }
            }
        };
    }

    private async Task SaveAsync()
    {
        try
        {
            var fiscalStart = (_fiscalYearStart.SelectedDate?.DateTime ?? GetFiscalYearStartFor(DateTime.Today)).Date;
            var plans = _rowControls
                .Select(row => new BudgetPlanInput(
                    row.MonthStart,
                    ParseAmount(row.SalesBudget.Text, $"{row.MonthStart:yyyy/MM} 売上予算"),
                    ParseAmount(row.ExpenseBudget.Text, $"{row.MonthStart:yyyy/MM} 支出予算"),
                    ParseAmount(row.ExpectedCashIn.Text, $"{row.MonthStart:yyyy/MM} 入金見込"),
                    ParseAmount(row.ExpectedCashOut.Text, $"{row.MonthStart:yyyy/MM} 出金見込"),
                    row.Note.Text))
                .ToList();

            _saveButton.IsEnabled = false;
            await _database.SaveBudgetPlansAsync(_user.CompanyId, _user.UserId, fiscalStart, plans);
            await LoadAsync();
            _message.Text = "予算実績 / 資金繰り見込を保存しました。";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _saveButton.IsEnabled = true;
        }
    }

    private DateTime GetFiscalYearStartFor(DateTime targetDate)
    {
        var template = _companyFiscalYearTemplate == default
            ? new DateTime(targetDate.Year, 1, 1)
            : _companyFiscalYearTemplate;
        var year = targetDate.Month > template.Month ||
                   (targetDate.Month == template.Month && targetDate.Day >= template.Day)
            ? targetDate.Year
            : targetDate.Year - 1;
        var day = Math.Min(template.Day, DateTime.DaysInMonth(year, template.Month));
        return new DateTime(year, template.Month, day);
    }

    private static decimal ParseAmount(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount < 0)
        {
            throw new InvalidOperationException($"{label}は0以上の数値で入力してください。");
        }

        return amount;
    }

    private void SetError(string message)
    {
        _message.Text = message;
        _message.Foreground = Brush.Parse("#B42318");
    }

    private static TextBox AmountInput(decimal amount)
    {
        return new TextBox
        {
            Text = amount == 0 ? "" : amount.ToString("N0"),
            HorizontalContentAlignment = HorizontalAlignment.Right,
            MinWidth = 96
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
                ColumnDefinitions = TableColumns(),
                Children =
                {
                    HeaderCell("月", 0),
                    HeaderCell("区分", 1),
                    HeaderCell("実績売上", 2, true),
                    HeaderCell("予算売上", 3, true),
                    HeaderCell("実績支出", 4, true),
                    HeaderCell("予算支出", 5, true),
                    HeaderCell("実績利益", 6, true),
                    HeaderCell("予算利益", 7, true),
                    HeaderCell("差異", 8, true),
                    HeaderCell("着地見込", 9, true),
                    HeaderCell("入金見込", 10, true),
                    HeaderCell("出金見込", 11, true),
                    HeaderCell("資金増減", 12, true),
                    HeaderCell("現預金見込", 13, true),
                    HeaderCell("メモ", 14)
                }
            }
        };
    }

    private static ColumnDefinitions TableColumns()
    {
        return new ColumnDefinitions("86,58,100,112,100,112,100,100,100,100,112,112,100,110,180");
    }

    private static Control Field(string label, Control input, double minWidth)
    {
        return new StackPanel
        {
            Spacing = 4,
            MinWidth = minWidth,
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
            Margin = new Thickness(0, 0, 8, 12),
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

    private static Control InputCell(Control input, int column)
    {
        Grid.SetColumn(input, column);
        return input;
    }

    private static Control AmountCell(decimal amount, int column, IBrush? brush = null)
    {
        var block = new TextBlock
        {
            Text = FormatAmount(amount),
            Foreground = brush ?? Brush.Parse("#243044"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static string FormatAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount):N0}"
            : amount.ToString("N0");
    }
}
