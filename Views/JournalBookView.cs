using System.Globalization;
using System.Text;
using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AccountingApp.Views;

public sealed class JournalBookView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly Action<string?, DateTime> _openJournalForm;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly TextBlock _monthLabel = ViewHelpers.Heading("", 20);
    private readonly TextBlock _message = new()
    {
        Text = "仕訳帳を読み込み中です。",
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Brush.Parse("#4A5568")
    };
    private readonly TextBlock _debitTotal = ViewHelpers.Body("0");
    private readonly TextBlock _creditTotal = ViewHelpers.Body("0");
    private readonly Button _previousMonthButton = ViewHelpers.SecondaryButton("前月");
    private readonly Button _importCsvButton = ViewHelpers.SecondaryButton("CSV取込");
    private readonly Button _exportCsvButton = ViewHelpers.SecondaryButton("CSV出力");
    private readonly Button _exportPdfButton = ViewHelpers.SecondaryButton("PDF出力");
    private readonly TextBox _entryNumberFilter = new() { PlaceholderText = "仕訳番号" };
    private readonly TextBox _keywordFilter = new() { PlaceholderText = "摘要・証憑番号・科目名" };
    private readonly TextBox _debitAccountFilter = new() { PlaceholderText = "借方科目" };
    private readonly TextBox _creditAccountFilter = new() { PlaceholderText = "貸方科目" };
    private readonly TextBox _minAmountFilter = new() { PlaceholderText = "下限金額" };
    private readonly TextBox _maxAmountFilter = new() { PlaceholderText = "上限金額" };
    private DateTime _targetMonth;
    private DateTime? _minimumMonth;
    private IReadOnlyList<JournalBookRow> _monthRows = Array.Empty<JournalBookRow>();
    private IReadOnlyList<JournalBookRow> _currentRows = Array.Empty<JournalBookRow>();
    private HashSet<string> _currentEntryNumbers = [];

    public JournalBookView(SqliteDatabase database, AppUser user, Action backToDashboard, Action<string?, DateTime> openJournalForm, DateTime initialTargetMonth)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _openJournalForm = openJournalForm;
        _targetMonth = new DateTime(initialTargetMonth.Year, initialTargetMonth.Month, 1);
        Content = Build();
        _ = LoadAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.Click += (_, _) => _backToDashboard();

        var newButton = ViewHelpers.PrimaryButton("新規仕訳");
        newButton.Width = 120;
        newButton.Click += (_, _) => _openJournalForm(null, _targetMonth);

        _importCsvButton.Width = 100;
        _importCsvButton.Click += async (_, _) => await ImportCsvAsync();

        _exportCsvButton.Width = 100;
        _exportCsvButton.Click += async (_, _) => await ExportCsvAsync();

        _exportPdfButton.Width = 100;
        _exportPdfButton.Click += async (_, _) => await ExportPdfAsync();

        _previousMonthButton.Width = 90;
        _previousMonthButton.Click += async (_, _) =>
        {
            if (_minimumMonth.HasValue && _targetMonth <= _minimumMonth.Value)
            {
                UpdateMonthNavigationState();
                return;
            }

            _targetMonth = _targetMonth.AddMonths(-1);
            await LoadAsync();
        };

        var nextButton = ViewHelpers.SecondaryButton("次月");
        nextButton.Width = 90;
        nextButton.Click += async (_, _) =>
        {
            _targetMonth = _targetMonth.AddMonths(1);
            await LoadAsync();
        };

        var currentButton = ViewHelpers.SecondaryButton("当月");
        currentButton.Width = 90;
        currentButton.Click += async (_, _) =>
        {
            _targetMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (_minimumMonth.HasValue && _targetMonth < _minimumMonth.Value)
            {
                _targetMonth = _minimumMonth.Value;
            }

            await LoadAsync();
        };

        var searchButton = ViewHelpers.SecondaryButton("検索");
        searchButton.Width = 100;
        searchButton.Click += (_, _) => ApplyFilters();

        var clearButton = ViewHelpers.SecondaryButton("条件クリア");
        clearButton.Width = 110;
        clearButton.Click += (_, _) =>
        {
            _entryNumberFilter.Text = "";
            _keywordFilter.Text = "";
            _debitAccountFilter.Text = "";
            _creditAccountFilter.Text = "";
            _minAmountFilter.Text = "";
            _maxAmountFilter.Text = "";
            ApplyFilters();
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,12,Auto,12,Auto,12,Auto,12,Auto"),
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        ViewHelpers.Heading(_user.CompanyName),
                        ViewHelpers.Body("仕訳帳")
                    }
                },
                _importCsvButton,
                _exportCsvButton,
                _exportPdfButton,
                newButton,
                backButton
            }
        };
        Grid.SetColumn(_importCsvButton, 1);
        Grid.SetColumn(_exportCsvButton, 3);
        Grid.SetColumn(_exportPdfButton, 5);
        Grid.SetColumn(newButton, 7);
        Grid.SetColumn(backButton, 9);

        var monthControls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,10,Auto,10,Auto,24,Auto,20,*,20,Auto,100,16,Auto,100"),
            Children =
            {
                _previousMonthButton,
                nextButton,
                currentButton,
                _monthLabel,
                _message,
                SummaryLabel("借方合計", 10),
                SummaryBox(_debitTotal, 11),
                SummaryLabel("貸方合計", 13),
                SummaryBox(_creditTotal, 14)
            }
        };
        Grid.SetColumn(nextButton, 2);
        Grid.SetColumn(currentButton, 4);
        Grid.SetColumn(_monthLabel, 6);
        Grid.SetColumn(_message, 8);

        var searchControls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,12,240,12,180,12,180,12,110,12,110,12,100,12,110"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 8,
            Children =
            {
                SearchField("仕訳番号", _entryNumberFilter, 0, 0),
                SearchField("キーワード", _keywordFilter, 2, 0),
                SearchField("借方科目", _debitAccountFilter, 4, 0),
                SearchField("貸方科目", _creditAccountFilter, 6, 0),
                SearchField("下限金額", _minAmountFilter, 8, 0),
                SearchField("上限金額", _maxAmountFilter, 10, 0),
                ButtonField(searchButton, 12, 0),
                ButtonField(clearButton, 14, 0)
            }
        };

        var controls = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                monthControls,
                searchControls
            }
        });

        var listScroll = new ScrollViewer { Content = _rows };
        Grid.SetRow(listScroll, 1);

        var list = ViewHelpers.Panel(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                JournalHeader(),
                listScroll
            }
        });

        var layout = new Grid
        {
            Margin = new Thickness(28),
            RowDefinitions = new RowDefinitions("Auto,18,Auto,18,*"),
            Children =
            {
                header,
                controls,
                list
            }
        };
        Grid.SetRow(controls, 2);
        Grid.SetRow(list, 4);
        return layout;
    }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            _minimumMonth = new DateTime(settings.FiscalYearStart.Year, settings.FiscalYearStart.Month, 1);
            if (_targetMonth < _minimumMonth.Value)
            {
                _targetMonth = _minimumMonth.Value;
            }

            UpdateMonthNavigationState();

            _monthLabel.Text = $"{_targetMonth:yyyy年M月}";
            var from = _targetMonth;
            var to = _targetMonth.AddMonths(1);
            _monthRows = await _database.GetJournalBookRowsAsync(_user.CompanyId, from, to);
            ApplyFilters();
        }
        catch (Exception ex)
        {
            _monthRows = Array.Empty<JournalBookRow>();
            _currentRows = Array.Empty<JournalBookRow>();
            _currentEntryNumbers = [];
            _rows.Children.Clear();
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private void ApplyFilters()
    {
        try
        {
            var minAmount = ParseAmount(_minAmountFilter.Text, "下限金額");
            var maxAmount = ParseAmount(_maxAmountFilter.Text, "上限金額");
            if (minAmount.HasValue && maxAmount.HasValue && minAmount.Value > maxAmount.Value)
            {
                throw new InvalidOperationException("下限金額は上限金額以下で入力してください。");
            }

            var entryNumberFilter = Normalize(_entryNumberFilter.Text);
            var keywordFilter = Normalize(_keywordFilter.Text);
            var debitAccountFilter = Normalize(_debitAccountFilter.Text);
            var creditAccountFilter = Normalize(_creditAccountFilter.Text);

            var filteredRows = _monthRows
                .GroupBy(x => x.EntryNumber, StringComparer.Ordinal)
                .Where(group => MatchesFilters(group, entryNumberFilter, keywordFilter, debitAccountFilter, creditAccountFilter, minAmount, maxAmount))
                .SelectMany(group => group)
                .ToList();

            _currentRows = filteredRows;
            _currentEntryNumbers = filteredRows
                .Select(x => x.EntryNumber)
                .ToHashSet(StringComparer.Ordinal);

            RenderRows();
        }
        catch (Exception ex)
        {
            _currentRows = Array.Empty<JournalBookRow>();
            _currentEntryNumbers = [];
            _rows.Children.Clear();
            _debitTotal.Text = "0";
            _creditTotal.Text = "0";
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private void RenderRows()
    {
        _rows.Children.Clear();

        if (_monthRows.Count == 0)
        {
            _message.Text = "この月の仕訳はありません。";
            _message.Foreground = Brush.Parse("#4A5568");
            _debitTotal.Text = "0";
            _creditTotal.Text = "0";
            return;
        }

        if (_currentRows.Count == 0)
        {
            _message.Text = "検索条件に一致する仕訳はありません。";
            _message.Foreground = Brush.Parse("#4A5568");
            _debitTotal.Text = "0";
            _creditTotal.Text = "0";
            return;
        }

        string? previousEntryNumber = null;
        string? previousDescription = null;
        foreach (var row in _currentRows)
        {
            var isVoucherStart = !string.Equals(previousEntryNumber, row.EntryNumber, StringComparison.Ordinal);
            var descriptionText = ResolveDescriptionText(row.Description, isVoucherStart, previousDescription);
            _rows.Children.Add(JournalRow(row, isVoucherStart, descriptionText));
            previousEntryNumber = row.EntryNumber;
            previousDescription = row.Description;
        }

        _debitTotal.Text = _currentRows.Sum(x => x.DebitAmount).ToString("N0");
        _creditTotal.Text = _currentRows.Sum(x => x.CreditAmount).ToString("N0");
        var voucherCount = _currentRows.Select(x => x.EntryNumber).Distinct(StringComparer.Ordinal).Count();
        var filterCount = CountActiveFilters();
        _message.Text = filterCount == 0
            ? $"{voucherCount:N0} 件の仕訳を表示しています。"
            : $"{voucherCount:N0} 件の仕訳を表示しています。検索条件: {filterCount} 件";
        _message.Foreground = Brush.Parse("#4A5568");
    }

    private static bool MatchesFilters(
        IGrouping<string, JournalBookRow> voucher,
        string? entryNumberFilter,
        string? keywordFilter,
        string? debitAccountFilter,
        string? creditAccountFilter,
        decimal? minAmount,
        decimal? maxAmount)
    {
        var rows = voucher.ToList();
        var firstRow = rows[0];
        var voucherAmount = rows.Sum(x => x.DebitAmount);

        if (!string.IsNullOrWhiteSpace(entryNumberFilter) &&
            !ContainsText(firstRow.EntryNumber, entryNumberFilter))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(keywordFilter))
        {
            var matchesKeyword = rows.Any(row =>
                ContainsText(row.EntryNumber, keywordFilter) ||
                ContainsText(row.Description, keywordFilter) ||
                ContainsText(row.Reference, keywordFilter) ||
                ContainsText(row.DebitAccountDisplay, keywordFilter) ||
                ContainsText(row.CreditAccountDisplay, keywordFilter));

            if (!matchesKeyword)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(debitAccountFilter) &&
            !rows.Any(row => ContainsText(row.DebitAccountDisplay, debitAccountFilter)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(creditAccountFilter) &&
            !rows.Any(row => ContainsText(row.CreditAccountDisplay, creditAccountFilter)))
        {
            return false;
        }

        if (minAmount.HasValue && voucherAmount < minAmount.Value)
        {
            return false;
        }

        if (maxAmount.HasValue && voucherAmount > maxAmount.Value)
        {
            return false;
        }

        return true;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool ContainsText(string? source, string keyword)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
    }

    private static decimal? ParseAmount(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) || value < 0)
        {
            throw new InvalidOperationException($"{label}は 0 以上の数値で入力してください。");
        }

        return value;
    }

    private int CountActiveFilters()
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(_entryNumberFilter.Text))
        {
            count++;
        }
        if (!string.IsNullOrWhiteSpace(_keywordFilter.Text))
        {
            count++;
        }
        if (!string.IsNullOrWhiteSpace(_debitAccountFilter.Text))
        {
            count++;
        }
        if (!string.IsNullOrWhiteSpace(_creditAccountFilter.Text))
        {
            count++;
        }
        if (!string.IsNullOrWhiteSpace(_minAmountFilter.Text))
        {
            count++;
        }
        if (!string.IsNullOrWhiteSpace(_maxAmountFilter.Text))
        {
            count++;
        }

        return count;
    }

    private static Control SearchField(string label, Control input, int column, int row)
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
        Grid.SetRow(panel, row);
        return panel;
    }

    private static Control ButtonField(Control button, int column, int row)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children =
            {
                new Border { Height = 28 },
                button
            }
        };
        Grid.SetColumn(panel, column);
        Grid.SetRow(panel, row);
        return panel;
    }

    private static Control JournalHeader()
    {
        return new Border
        {
            Background = Brush.Parse("#E6E9ED"),
            BorderBrush = Brush.Parse("#8A8F96"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            Child = new Grid
            {
                ColumnDefinitions = JournalColumns(),
                Children =
                {
                    HeaderCell("日付", 0),
                    HeaderCell("仕訳番号", 1),
                    HeaderCell("摘要", 2),
                    HeaderCell("証憑番号", 3),
                    HeaderCell("借方科目", 4),
                    HeaderCell("貸方科目", 5),
                    HeaderCell("借方金額", 6),
                    HeaderCell("貸方金額", 7),
                    HeaderCell("操作", 8)
                }
            }
        };
    }

    private Control JournalRow(JournalBookRow rowData, bool isVoucherStart, string descriptionText)
    {
        var editButton = ViewHelpers.SecondaryButton("編集");
        editButton.Width = 70;
        editButton.Click += (_, _) => _openJournalForm(rowData.EntryNumber, _targetMonth);

        var deleteButton = CreateDeleteButton();
        deleteButton.Width = 70;
        deleteButton.Click += async (_, _) => await ConfirmAndDeleteAsync(rowData.EntryNumber, deleteButton);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                editButton,
                deleteButton
            }
        };

        var row = new Grid
        {
            ColumnDefinitions = JournalColumns(),
            Children =
            {
                Cell(isVoucherStart ? rowData.EntryDate.ToString("yyyy-MM-dd") : "", 0),
                Cell(isVoucherStart ? rowData.EntryNumber : "", 1, FontWeight.SemiBold),
                Cell(descriptionText, 2),
                Cell(isVoucherStart ? rowData.Reference ?? "" : "", 3),
                Cell(rowData.DebitAccountDisplay ?? "", 4),
                Cell(rowData.CreditAccountDisplay ?? "", 5),
                AmountCell(rowData.DebitAmount, 6),
                AmountCell(rowData.CreditAmount, 7),
                actions
            }
        };
        Grid.SetColumn(actions, 8);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = isVoucherStart ? new Thickness(0, 2, 0, 1) : new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = row
        };
    }

    private static string ResolveDescriptionText(string? description, bool isVoucherStart, string? previousDescription)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        if (!isVoucherStart && string.Equals(description, previousDescription, StringComparison.Ordinal))
        {
            return "〃";
        }

        return description;
    }

    private void UpdateMonthNavigationState()
    {
        _previousMonthButton.IsEnabled = !_minimumMonth.HasValue || _targetMonth > _minimumMonth.Value;
    }

    private async Task ExportCsvAsync()
    {
        if (_currentRows.Count == 0)
        {
            _message.Text = "CSV出力する仕訳データがありません。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            _message.Text = "保存ダイアログを開けませんでした。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "仕訳帳CSVを保存",
            SuggestedFileName = $"journal_{_targetMonth:yyyyMM}.csv",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV")
                {
                    Patterns = ["*.csv"],
                    MimeTypes = ["text/csv", "application/csv", "text/plain"]
                }
            ],
            ShowOverwritePrompt = true
        });

        if (file is null)
        {
            return;
        }

        try
        {
            _exportCsvButton.IsEnabled = false;
            var rows = await _database.GetJournalCsvRowsAsync(_user.CompanyId, _targetMonth, _targetMonth.AddMonths(1));
            var filteredRows = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.EntryNumber) && _currentEntryNumbers.Contains(x.EntryNumber))
                .ToList();
            var csv = JournalCsvSerializer.Serialize(filteredRows);
            await File.WriteAllTextAsync(file.Path.LocalPath, csv, new UTF8Encoding(false));
            _message.Text = $"CSVを出力しました: {file.Name}";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            _exportCsvButton.IsEnabled = true;
        }
    }

    private async Task ImportCsvAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            _message.Text = "ファイル選択を開けませんでした。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "仕訳帳CSVを選択",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV")
                {
                    Patterns = ["*.csv"],
                    MimeTypes = ["text/csv", "application/csv", "text/plain"]
                },
                FilePickerFileTypes.All
            ]
        });

        var path = files.Count > 0 ? files[0].Path.LocalPath : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _importCsvButton.IsEnabled = false;
            var csvText = await File.ReadAllTextAsync(path, Encoding.UTF8);
            var rows = JournalCsvSerializer.Deserialize(csvText);
            await _database.ImportJournalCsvAsync(_user.CompanyId, _user.UserId, rows, _targetMonth);
            await LoadAsync();
            _message.Text = $"CSVを取り込みました: {Path.GetFileName(path)}";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            _importCsvButton.IsEnabled = true;
        }
    }

    private async Task ExportPdfAsync()
    {
        if (_currentRows.Count == 0)
        {
            _message.Text = "出力する仕訳データがありません。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            _message.Text = "保存ダイアログを開けませんでした。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "仕訳帳PDFを保存",
            SuggestedFileName = $"仕訳帳_{_targetMonth:yyyyMM}.pdf",
            DefaultExtension = "pdf",
            FileTypeChoices =
            [
                new FilePickerFileType("PDF")
                {
                    Patterns = ["*.pdf"],
                    MimeTypes = ["application/pdf"]
                }
            ],
            ShowOverwritePrompt = true
        });

        if (file is null)
        {
            return;
        }

        try
        {
            _exportPdfButton.IsEnabled = false;
            var error = await JournalBookPdfExporter.ExportAsync(file.Path.LocalPath, _user.CompanyName, _targetMonth, _currentRows);
            if (!string.IsNullOrWhiteSpace(error))
            {
                _message.Text = error;
                _message.Foreground = Brush.Parse("#B42318");
                return;
            }

            var previewError = PdfPreviewLauncher.Open(file.Path.LocalPath);
            _message.Text = previewError ?? $"PDFを出力しました: {file.Name}";
            _message.Foreground = previewError is null ? Brush.Parse("#1E6B52") : Brush.Parse("#B8860B");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            _exportPdfButton.IsEnabled = true;
        }
    }

    private static ColumnDefinitions JournalColumns()
    {
        return new ColumnDefinitions("110,140,220,140,180,180,110,110,170");
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

    private static Button CreateDeleteButton()
    {
        var button = ViewHelpers.SecondaryButton("削除");
        button.Background = Brush.Parse("#B42318");
        button.Foreground = Brushes.White;
        return button;
    }

    private async Task ConfirmAndDeleteAsync(string entryNumber, Button deleteButton)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            _message.Text = "削除確認ダイアログを表示できませんでした。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        var executeButton = ViewHelpers.PrimaryButton("削除する");
        executeButton.Width = 120;
        executeButton.Background = Brush.Parse("#B42318");

        var cancelButton = ViewHelpers.SecondaryButton("キャンセル");
        cancelButton.Width = 120;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { executeButton, cancelButton }
        };

        var dialog = new Window
        {
            Title = "仕訳削除確認",
            Width = 520,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = ViewHelpers.Panel(new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 18,
                Children =
                {
                    ViewHelpers.Heading("この仕訳を削除しますか", 22),
                    ViewHelpers.Body($"仕訳番号: {entryNumber}"),
                    ViewHelpers.Body("削除するとその仕訳に含まれるすべての明細が削除され、元に戻せません。"),
                    buttons
                }
            })
        };

        executeButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
        {
            return;
        }

        try
        {
            deleteButton.IsEnabled = false;
            await _database.DeleteJournalVoucherAsync(_user.CompanyId, _user.UserId, entryNumber);
            await LoadAsync();
            _message.Text = $"仕訳を削除しました: {entryNumber}";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            deleteButton.IsEnabled = true;
        }
    }
}
