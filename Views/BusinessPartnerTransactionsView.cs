using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class BusinessPartnerTransactionsView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly Action<string?> _openJournalForm;
    private readonly ComboBox _partner = new() { MinWidth = 280 };
    private readonly DatePicker _fromDate = new();
    private readonly DatePicker _toDate = new();
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly TextBlock _message = ViewHelpers.Body("取引先別取引一覧を読み込み中です。");
    private readonly TextBlock _debitTotal = ViewHelpers.Body("0");
    private readonly TextBlock _creditTotal = ViewHelpers.Body("0");
    private readonly List<BusinessPartner> _partners = [];
    private DateTime _minimumDate;
    private bool _isInitializing;
    private bool _isAdjustingDateRange;

    public BusinessPartnerTransactionsView(
        SqliteDatabase database,
        AppUser user,
        Action backToDashboard,
        Action<string?> openJournalForm)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _openJournalForm = openJournalForm;
        Content = Build();
        _partner.SelectionChanged += async (_, _) => await LoadTransactionsAsync();
        _fromDate.SelectedDateChanged += async (_, _) => await HandleFromDateChangedAsync();
        _toDate.SelectedDateChanged += async (_, _) => await HandleDateChangedAsync();
        _ = InitializeAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.Click += (_, _) => _backToDashboard();

        var newButton = ViewHelpers.PrimaryButton("新規仕訳");
        newButton.Width = 120;
        newButton.Click += (_, _) => _openJournalForm(null);

        var refreshButton = ViewHelpers.SecondaryButton("再表示");
        refreshButton.Width = 100;
        refreshButton.Height = 32;
        refreshButton.VerticalAlignment = VerticalAlignment.Bottom;
        refreshButton.Click += async (_, _) => await LoadTransactionsAsync();

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
                        ViewHelpers.Body("取引先別取引一覧")
                    }
                },
                newButton,
                backButton
            }
        };
        Grid.SetColumn(newButton, 1);
        Grid.SetColumn(backButton, 3);

        var filterRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                WithMargin(Field("取引先", _partner), new Thickness(0, 0, 16, 12)),
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
            _partner.ItemTemplate = PartnerTemplate;

            var settings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            _minimumDate = settings.FiscalYearStart.Date;

            _partners.Clear();
            _partners.AddRange((await _database.GetBusinessPartnersAsync(_user.CompanyId))
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Code)
                .ThenBy(x => x.Name));

            _partner.ItemsSource = _partners;
            _partner.SelectedItem = _partners.FirstOrDefault();

            _fromDate.SelectedDate = new DateTimeOffset(_minimumDate);
            _toDate.SelectedDate = new DateTimeOffset(GetDefaultToDate());

            if (_partners.Count == 0)
            {
                _message.Text = "取引先マスタがありません。先に取引先を登録してください。";
                _message.Foreground = Brush.Parse("#B42318");
                return;
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return;
        }
        finally
        {
            _isInitializing = false;
        }

        await LoadTransactionsAsync();
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
            var fromDate = GetSelectedFromDate();
            if (fromDate < _minimumDate)
            {
                fromDate = _minimumDate;
                _fromDate.SelectedDate = new DateTimeOffset(fromDate);
            }

            var toDate = GetSelectedToDate();
            if (toDate < fromDate)
            {
                _toDate.SelectedDate = new DateTimeOffset(fromDate);
            }
        }
        finally
        {
            _isAdjustingDateRange = false;
        }

        await LoadTransactionsAsync();
    }

    private async Task HandleDateChangedAsync()
    {
        if (_isInitializing || _isAdjustingDateRange)
        {
            return;
        }

        await LoadTransactionsAsync();
    }

    private async Task LoadTransactionsAsync()
    {
        if (_partner.SelectedItem is not BusinessPartner partner)
        {
            return;
        }

        try
        {
            var fromDate = GetSelectedFromDate();
            var toDate = GetSelectedToDate();
            if (toDate < fromDate)
            {
                toDate = fromDate;
                _toDate.SelectedDate = new DateTimeOffset(toDate);
            }

            var rows = await _database.GetBusinessPartnerTransactionLinesAsync(_user.CompanyId, partner.PartnerId, fromDate, toDate);
            _rows.Children.Clear();

            if (rows.Count == 0)
            {
                _debitTotal.Text = "0";
                _creditTotal.Text = "0";
                _message.Text = $"{partner.Code} {partner.Name} の取引は {fromDate:yyyy/MM/dd} から {toDate:yyyy/MM/dd} の期間にありません。";
                _message.Foreground = Brush.Parse("#4A5568");
                return;
            }

            foreach (var row in rows)
            {
                _rows.Children.Add(TransactionRow(row));
            }

            _debitTotal.Text = rows.Sum(x => x.Debit).ToString("N0");
            _creditTotal.Text = rows.Sum(x => x.Credit).ToString("N0");
            _message.Text = $"{partner.Code} {partner.Name} の取引を {rows.Count:N0} 行表示しています。期間: {fromDate:yyyy/MM/dd} - {toDate:yyyy/MM/dd}";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _rows.Children.Clear();
            _debitTotal.Text = "0";
            _creditTotal.Text = "0";
            SetError(ex.Message);
        }
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
            ColumnDefinitions = new ColumnDefinitions("Auto,110,16,Auto,110"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SummaryLabel("借方合計", 0),
                SummaryBox(_debitTotal, 1),
                SummaryLabel("貸方合計", 3),
                SummaryBox(_creditTotal, 4)
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
                    HeaderCell("日付", 0),
                    HeaderCell("仕訳番号", 1),
                    HeaderCell("勘定科目", 2),
                    HeaderCell("相手科目", 3),
                    HeaderCell("摘要", 4),
                    HeaderCell("参照/請求書", 5),
                    HeaderCell("借方", 6),
                    HeaderCell("貸方", 7),
                    HeaderCell("操作", 8)
                }
            }
        };
        Grid.SetRow(header, 0);
        return header;
    }

    private static ColumnDefinitions TableColumns()
    {
        return new ColumnDefinitions("100,130,220,220,*,180,110,110,80");
    }

    private static TextBlock HeaderCell(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#172033"),
            HorizontalAlignment = column is 6 or 7 ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private Control TransactionRow(BusinessPartnerTransactionLine line)
    {
        var editButton = ViewHelpers.SecondaryButton("編集");
        editButton.Width = 70;
        editButton.Click += (_, _) => _openJournalForm(line.EntryNumber);

        var row = new Grid
        {
            ColumnDefinitions = TableColumns(),
            Children =
            {
                Cell(line.EntryDate.ToString("yyyy-MM-dd"), 0),
                Cell(line.EntryNumber, 1, FontWeight.SemiBold),
                Cell(AccountText(line), 2),
                Cell(CounterpartText(line), 3),
                Cell(line.Description ?? string.Empty, 4),
                Cell(ReferenceInvoiceText(line), 5),
                AmountCell(line.Debit, 6),
                AmountCell(line.Credit, 7),
                editButton
            }
        };
        Grid.SetColumn(editButton, 8);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = row
        };
    }

    private DateTime GetSelectedFromDate()
    {
        var selected = (_fromDate.SelectedDate?.DateTime ?? _minimumDate).Date;
        return selected < _minimumDate ? _minimumDate : selected;
    }

    private DateTime GetSelectedToDate()
    {
        var selected = (_toDate.SelectedDate?.DateTime ?? GetDefaultToDate()).Date;
        return selected < _minimumDate ? _minimumDate : selected;
    }

    private DateTime GetDefaultToDate()
    {
        var today = DateTime.Today;
        return today < _minimumDate ? _minimumDate : today;
    }

    private static string AccountText(BusinessPartnerTransactionLine line)
    {
        if (string.IsNullOrWhiteSpace(line.AccountCode))
        {
            return string.Empty;
        }

        var account = $"{line.AccountCode} {line.AccountName}";
        if (string.IsNullOrWhiteSpace(line.SubAccountCode))
        {
            return account;
        }

        return $"{account} / {line.SubAccountCode} {line.SubAccountName}";
    }

    private static string CounterpartText(BusinessPartnerTransactionLine line)
    {
        if (string.IsNullOrWhiteSpace(line.CounterpartAccountCode))
        {
            return string.Empty;
        }

        var account = $"{line.CounterpartAccountCode} {line.CounterpartAccountName}";
        if (string.IsNullOrWhiteSpace(line.CounterpartSubAccountCode))
        {
            return account;
        }

        return $"{account} / {line.CounterpartSubAccountCode} {line.CounterpartSubAccountName}";
    }

    private static string ReferenceInvoiceText(BusinessPartnerTransactionLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Reference))
        {
            return line.InvoiceNumber ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(line.InvoiceNumber)
            ? line.Reference
            : $"{line.Reference} / {line.InvoiceNumber}";
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
            Text = amount == 0 ? string.Empty : amount.ToString("N0"),
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

    private static readonly IDataTemplate PartnerTemplate = new FuncDataTemplate<BusinessPartner>((partner, _) =>
        new TextBlock
        {
            Text = partner is null
                ? string.Empty
                : partner.IsActive
                    ? $"{partner.Code} {partner.Name}"
                    : $"{partner.Code} {partner.Name} (停止)",
            Foreground = Brush.Parse("#243044")
        });
}
