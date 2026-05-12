using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class CashbookView : UserControl
{
    private sealed record SubAccountFilterOption(int? SubAccountId, string Label, decimal? Balance = null)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly Action<string?> _openJournalForm;
    private readonly ComboBox _account = new() { MinWidth = 220 };
    private readonly ComboBox _subAccount = new() { IsEnabled = false, MinWidth = 280 };
    private readonly StackPanel _lines = new() { Spacing = 0 };
    private readonly TextBlock _periodLabel = ViewHelpers.Heading("", 18);
    private readonly TextBlock _message = ViewHelpers.Body("出納帳を読み込みます。");
    private readonly TextBlock _carryForward = ViewHelpers.Body("0");
    private readonly TextBlock _receiptTotal = ViewHelpers.Body("0");
    private readonly TextBlock _paymentTotal = ViewHelpers.Body("0");
    private readonly TextBlock _endingBalance = ViewHelpers.Body("0");
    private readonly Button _previousPeriodButton = ViewHelpers.SecondaryButton("前月");
    private readonly List<Account> _accounts = [];
    private readonly List<SubAccount> _subAccounts = [];
    private DateTime _periodStart;
    private DateTime _periodEnd;
    private DateTime _minimumDate;
    private int _closingDay = 31;

    public CashbookView(SqliteDatabase database, AppUser user, Action backToDashboard, Action<string?> openJournalForm)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _openJournalForm = openJournalForm;
        Content = Build();
        _account.SelectionChanged += async (_, _) => await AccountChangedAsync();
        _subAccount.SelectionChanged += async (_, _) => await LoadLedgerAsync();
        _ = LoadAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.Click += (_, _) => _backToDashboard();

        var newButton = ViewHelpers.PrimaryButton("仕訳を新規登録");
        newButton.Width = 160;
        newButton.Click += (_, _) => _openJournalForm(null);

        var refreshButton = ViewHelpers.SecondaryButton("再表示");
        refreshButton.Width = 100;
        refreshButton.Click += async (_, _) => await LoadLedgerAsync();

        _previousPeriodButton.Width = 90;
        _previousPeriodButton.Click += async (_, _) =>
        {
            (_periodStart, _periodEnd) = ClampPeriodToMinimum(
                GetPeriodRangeContaining(_periodStart.AddDays(-1), _closingDay),
                _minimumDate,
                _closingDay);
            await LoadLedgerAsync();
        };

        var nextPeriodButton = ViewHelpers.SecondaryButton("次月");
        nextPeriodButton.Width = 90;
        nextPeriodButton.Click += async (_, _) =>
        {
            (_periodStart, _periodEnd) = GetPeriodRangeContaining(_periodEnd.AddDays(1), _closingDay);
            await LoadLedgerAsync();
        };

        var currentPeriodButton = ViewHelpers.SecondaryButton("当月");
        currentPeriodButton.Width = 90;
        currentPeriodButton.Click += async (_, _) =>
        {
            (_periodStart, _periodEnd) = ClampPeriodToMinimum(
                GetPeriodRangeContaining(DateTime.Today, _closingDay),
                _minimumDate,
                _closingDay);
            await LoadLedgerAsync();
        };

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
                        ViewHelpers.Body("現金出納帳 / 科目別出納帳")
                    }
                },
                newButton,
                backButton
            }
        };
        Grid.SetColumn(newButton, 1);
        Grid.SetColumn(backButton, 3);

        var filterRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,16,280,16,*,16,100"),
            Children =
            {
                Field("対象科目", _account, 0),
                Field("補助科目", _subAccount, 2),
                SummaryPanelWithCarryForward(),
                refreshButton
            }
        };
        Grid.SetColumn(refreshButton, 6);

        var periodRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,10,Auto,10,Auto,24,Auto,20,*"),
            Children =
            {
                _previousPeriodButton,
                nextPeriodButton,
                currentPeriodButton,
                _periodLabel
            }
        };
        Grid.SetColumn(nextPeriodButton, 2);
        Grid.SetColumn(currentPeriodButton, 4);
        Grid.SetColumn(_periodLabel, 6);

        var controls = ViewHelpers.Panel(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,12,Auto"),
            Children =
            {
                filterRow,
                periodRow
            }
        });
        Grid.SetRow(periodRow, 2);

        var ledgerRows = new ScrollViewer { Content = _lines };
        Grid.SetRow(ledgerRows, 2);

        var ledger = ViewHelpers.Panel(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Children =
            {
                LedgerHeader(),
                _message,
                ledgerRows
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
                ledger
            }
        };
        Grid.SetRow(controls, 2);
        Grid.SetRow(ledger, 4);
        return layout;
    }

    private Control Field(string label, Control input, int column)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                ViewHelpers.Label(label),
                input
            }
        };
        Grid.SetColumn(panel, column);
        return panel;
    }

    private Control SummaryPanelWithCarryForward()
    {
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,100,16,Auto,100,16,Auto,100,16,Auto,100"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children =
            {
                SummaryLabel("前月残高", 0),
                SummaryBox(_carryForward, 1),
                SummaryLabel("入金合計", 3),
                SummaryBox(_receiptTotal, 4),
                SummaryLabel("出金合計", 6),
                SummaryBox(_paymentTotal, 7),
                SummaryLabel("残高", 9),
                SummaryBox(_endingBalance, 10)
            }
        };
        Grid.SetColumn(panel, 4);
        return panel;
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

    private static Control LedgerHeader()
    {
        var header = new Border
        {
            Background = Brush.Parse("#E6E9ED"),
            BorderBrush = Brush.Parse("#8A8F96"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            Child = new Grid
            {
                ColumnDefinitions = LedgerColumns(),
                Children =
                {
                    HeaderCell("日付", 0),
                    HeaderCell("伝票番号", 1),
                    HeaderCell("相手科目", 2),
                    HeaderCell("摘要", 3),
                    HeaderCell("参照", 4),
                    HeaderCell("取引先/請求書", 5),
                    HeaderCell("入金", 6),
                    HeaderCell("出金", 7),
                    HeaderCell("残高", 8),
                    HeaderCell("操作", 9)
                }
            }
        };
        Grid.SetRow(header, 0);
        return header;
    }

    private static ColumnDefinitions LedgerColumns()
    {
        return new ColumnDefinitions("100,130,200,*,110,180,110,110,110,80");
    }

    private static TextBlock HeaderCell(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#172033"),
            HorizontalAlignment = column >= 6 && column <= 8 ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private async Task LoadAsync()
    {
        try
        {
            _account.ItemTemplate = AccountTemplate;
            _subAccount.ItemTemplate = SubAccountFilterTemplate;

            _accounts.Clear();
            _accounts.AddRange((await _database.GetAccountsAsync(_user.CompanyId))
                .Where(IsCashbookAccount)
                .OrderBy(x => x.Code)
                .ThenBy(x => x.Name));

            _subAccounts.Clear();
            _subAccounts.AddRange(await _database.GetSubAccountsAsync(_user.CompanyId));

            var settings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            _closingDay = settings.ClosingDay;
            _minimumDate = settings.FiscalYearStart.Date;
            (_periodStart, _periodEnd) = ClampPeriodToMinimum(
                GetPeriodRangeContaining(DateTime.Today, _closingDay),
                _minimumDate,
                _closingDay);
            _periodLabel.Text = FormatPeriodLabel();
            UpdatePeriodNavigationState();

            _account.ItemsSource = _accounts;
            var cash = _accounts.FirstOrDefault(x => x.Code == "1010")
                ?? _accounts.FirstOrDefault(x => x.Name.Contains("現金", StringComparison.Ordinal))
                ?? _accounts.FirstOrDefault(x => x.Name.Contains("普通預金", StringComparison.Ordinal))
                ?? _accounts.FirstOrDefault(x => x.AccountType == "asset")
                ?? _accounts.FirstOrDefault();
            _account.SelectedItem = cash;

            if (cash is null)
            {
                _message.Text = $"会社ID {_user.CompanyId} に勘定科目が登録されていません。勘定科目マスタを確認してください。";
                return;
            }

            _message.Text = $"勘定科目 {_accounts.Count:N0} 件を読み込みました。";
            await AccountChangedAsync();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private async Task AccountChangedAsync()
    {
        if (_account.SelectedItem is not Account account)
        {
            _subAccount.ItemsSource = Array.Empty<SubAccountFilterOption>();
            _subAccount.SelectedIndex = -1;
            _subAccount.IsEnabled = false;
            return;
        }

        var choices = _subAccounts
            .Where(x => x.AccountId == account.AccountId && x.IsActive)
            .OrderBy(x => x.Code)
            .ToList();

        var filterOptions = new List<SubAccountFilterOption>
        {
            new(null, "合算表示")
        };
        filterOptions.AddRange(choices.Select(x => new SubAccountFilterOption(x.SubAccountId, $"{x.Code} {x.Name}", x.Balance)));

        _subAccount.ItemsSource = filterOptions;
        _subAccount.SelectedIndex = 0;
        _subAccount.IsEnabled = choices.Count > 0;

        await LoadLedgerAsync();
    }

    private async Task LoadLedgerAsync()
    {
        if (_account.SelectedItem is not Account account)
        {
            return;
        }

        try
        {
            (_periodStart, _periodEnd) = ClampPeriodToMinimum((_periodStart, _periodEnd), _minimumDate, _closingDay);
            _periodLabel.Text = FormatPeriodLabel();
            UpdatePeriodNavigationState();

            var subAccount = _subAccount.SelectedItem as SubAccountFilterOption;
            var carryForward = await _database.GetCashbookOpeningBalanceAsync(_user.CompanyId, account.AccountId, subAccount?.SubAccountId, _periodStart);
            var ledgerLines = await _database.GetCashbookLinesAsync(_user.CompanyId, account.AccountId, subAccount?.SubAccountId, _periodStart, _periodEnd);
            _lines.Children.Clear();
            _carryForward.Text = carryForward.ToString("N0");

            if (ledgerLines.Count == 0)
            {
                _message.Text = $"{FormatPeriodLabel()} の出納帳はありません。";
                _receiptTotal.Text = "0";
                _paymentTotal.Text = "0";
                _endingBalance.Text = carryForward.ToString("N0");
                _message.Foreground = Brush.Parse("#4A5568");
                return;
            }

            foreach (var line in ledgerLines)
            {
                _lines.Children.Add(LedgerRow(line));
            }

            _receiptTotal.Text = ledgerLines.Sum(x => x.Receipt).ToString("N0");
            _paymentTotal.Text = ledgerLines.Sum(x => x.Payment).ToString("N0");
            _endingBalance.Text = ledgerLines.Last().Balance.ToString("N0");
            _message.Text = $"{FormatPeriodLabel()} を {ledgerLines.Count:N0} 行表示しています。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private Control LedgerRow(CashbookLine line)
    {
        var editButton = ViewHelpers.SecondaryButton("変更");
        editButton.Width = 70;
        editButton.Click += (_, _) => _openJournalForm(line.EntryNumber);

        var row = new Grid
        {
            ColumnDefinitions = LedgerColumns(),
            Children =
            {
                Cell(line.EntryDate.ToString("yyyy-MM-dd"), 0),
                Cell(line.EntryNumber, 1, FontWeight.SemiBold),
                Cell(CounterpartText(line), 2),
                Cell(line.Description ?? "", 3),
                Cell(line.Reference ?? "", 4),
                Cell(PartnerInvoiceText(line), 5),
                AmountCell(line.Receipt, 6),
                AmountCell(line.Payment, 7),
                AmountCell(line.Balance, 8),
                editButton
            }
        };
        Grid.SetColumn(editButton, 9);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = row
        };
    }

    private static string CounterpartText(CashbookLine line)
    {
        if (string.IsNullOrWhiteSpace(line.CounterpartAccountCode))
        {
            return "";
        }

        var account = $"{line.CounterpartAccountCode} {line.CounterpartAccountName}";
        if (string.IsNullOrWhiteSpace(line.CounterpartSubAccountCode))
        {
            return account;
        }

        return $"{account} / {line.CounterpartSubAccountCode} {line.CounterpartSubAccountName}";
    }

    private static string PartnerInvoiceText(CashbookLine line)
    {
        var partner = string.IsNullOrWhiteSpace(line.PartnerCode)
            ? line.PartnerName ?? ""
            : $"{line.PartnerCode} {line.PartnerName}";

        if (string.IsNullOrWhiteSpace(line.InvoiceNumber))
        {
            return partner;
        }

        return string.IsNullOrWhiteSpace(partner)
            ? line.InvoiceNumber
            : $"{partner} / {line.InvoiceNumber}";
    }

    private string FormatPeriodLabel()
    {
        return $"{_periodStart:yyyy/MM/dd} - {_periodEnd:yyyy/MM/dd}";
    }

    private void UpdatePeriodNavigationState()
    {
        _previousPeriodButton.IsEnabled = _periodStart > _minimumDate;
    }

    private static (DateTime PeriodStart, DateTime PeriodEnd) GetPeriodRangeContaining(DateTime referenceDate, int closingDay)
    {
        var normalizedClosingDay = Math.Clamp(closingDay, 1, 31);
        var currentMonthClosing = CreateClosingDate(referenceDate.Year, referenceDate.Month, normalizedClosingDay);
        if (referenceDate.Date <= currentMonthClosing)
        {
            var previousClosing = CreateClosingDate(referenceDate.AddMonths(-1).Year, referenceDate.AddMonths(-1).Month, normalizedClosingDay);
            return (previousClosing.AddDays(1), currentMonthClosing);
        }

        var nextClosing = CreateClosingDate(referenceDate.AddMonths(1).Year, referenceDate.AddMonths(1).Month, normalizedClosingDay);
        return (currentMonthClosing.AddDays(1), nextClosing);
    }

    private static DateTime CreateClosingDate(int year, int month, int closingDay)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Min(closingDay, lastDay));
    }

    private static (DateTime PeriodStart, DateTime PeriodEnd) ClampPeriodToMinimum(
        (DateTime PeriodStart, DateTime PeriodEnd) period,
        DateTime minimumDate,
        int closingDay)
    {
        if (minimumDate == default)
        {
            return period;
        }

        if (period.PeriodEnd < minimumDate)
        {
            period = GetPeriodRangeContaining(minimumDate, closingDay);
        }

        if (period.PeriodStart < minimumDate)
        {
            return (minimumDate, period.PeriodEnd);
        }

        return period;
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

    private static Control AmountCell(decimal amount, int column)
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

    private void SetError(string text)
    {
        _message.Text = text;
        _message.Foreground = Brush.Parse("#B42318");
    }

    private static bool IsCashbookAccount(Account account)
    {
        return int.TryParse(account.Code, out var codeValue) && codeValue < 1500;
    }

    private static readonly IDataTemplate AccountTemplate = new FuncDataTemplate<Account>((account, _) =>
        new TextBlock
        {
            Text = account is null ? "" : $"{account.Code} {account.Name}",
            Foreground = Brush.Parse("#243044")
        });

    private static readonly IDataTemplate SubAccountFilterTemplate = new FuncDataTemplate<SubAccountFilterOption>((subAccount, _) =>
        new TextBlock
        {
            Text = subAccount?.Label ?? "",
            Foreground = Brush.Parse("#243044")
        });
}
