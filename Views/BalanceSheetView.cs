using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class BalanceSheetView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly DatePicker _asOfDate = new();
    private readonly StackPanel _assetsRows = new() { Spacing = 0 };
    private readonly StackPanel _liabilitiesRows = new() { Spacing = 0 };
    private readonly TextBlock _assetsTotal = ViewHelpers.Body("0");
    private readonly TextBlock _liabilitiesTotal = ViewHelpers.Body("0");
    private readonly TextBlock _equityTotal = ViewHelpers.Body("0");
    private readonly TextBlock _currentPeriodNetIncome = ViewHelpers.Body("0");
    private readonly TextBlock _liabilitiesAndEquityTotal = ViewHelpers.Body("0");
    private readonly TextBlock _balanceCheck = ViewHelpers.Body("一致確認を計算中です。");
    private readonly TextBlock _message = ViewHelpers.Body("貸借対照表を読み込み中です。");
    private readonly Button _exportPdfButton = ViewHelpers.SecondaryButton("PDF出力");
    private BalanceSheetSummary? _currentSummary;
    private bool _isInitializing;

    public BalanceSheetView(SqliteDatabase database, AppUser user, Action backToDashboard)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        Content = Build();
        _asOfDate.SelectedDateChanged += async (_, _) => await HandleDateChangedAsync();
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
        refreshButton.Click += async (_, _) => await LoadBalanceSheetAsync();

        _exportPdfButton.Width = 100;
        _exportPdfButton.Height = 32;
        _exportPdfButton.Click += async (_, _) => await ExportPdfAsync();

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
                        ViewHelpers.Body("貸借対照表")
                    }
                },
                _exportPdfButton,
                backButton
            }
        };
        Grid.SetColumn(_exportPdfButton, 1);
        Grid.SetColumn(backButton, 3);

        var filterRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                WithMargin(Field("基準日", _asOfDate), new Thickness(0, 0, 16, 12)),
                ButtonField(refreshButton)
            }
        };

        var summaryPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,16,Auto,120,16,Auto,120,16,Auto,120,16,Auto,120"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SummaryLabel("資産合計", 0),
                SummaryBox(_assetsTotal, 1),
                SummaryLabel("負債合計", 3),
                SummaryBox(_liabilitiesTotal, 4),
                SummaryLabel("資本合計", 6),
                SummaryBox(_equityTotal, 7),
                SummaryLabel("当期純損益", 9),
                SummaryBox(_currentPeriodNetIncome, 10),
                SummaryLabel("負債・資本合計", 12),
                SummaryBox(_liabilitiesAndEquityTotal, 13)
            }
        };

        var controls = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                filterRow,
                summaryPanel,
                _balanceCheck
            }
        });

        var assetPanel = CreateSectionPanel("資産の部", _assetsRows);
        var liabilityPanel = CreateSectionPanel("負債・資本の部", _liabilitiesRows);

        var bodyColumns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,18,*"),
            Children =
            {
                assetPanel,
                liabilityPanel
            }
        };
        Grid.SetColumn(liabilityPanel, 2);

        var body = new ScrollViewer
        {
            Content = bodyColumns
        };

        var layout = new Grid
        {
            Margin = new Thickness(28),
            RowDefinitions = new RowDefinitions("Auto,18,Auto,18,Auto,18,*"),
            Children =
            {
                header,
                controls,
                _message,
                body
            }
        };
        Grid.SetRow(controls, 2);
        Grid.SetRow(_message, 4);
        Grid.SetRow(body, 6);
        return layout;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _isInitializing = true;
            var closingDay = await _database.GetCompanyClosingDayAsync(_user.CompanyId);
            _asOfDate.SelectedDate = new DateTimeOffset(GetLatestClosedDate(DateTime.Today, closingDay));
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

        await LoadBalanceSheetAsync();
    }

    private async Task HandleDateChangedAsync()
    {
        if (_isInitializing)
        {
            return;
        }

        await LoadBalanceSheetAsync();
    }

    private async Task LoadBalanceSheetAsync()
    {
        var asOfDate = (_asOfDate.SelectedDate?.DateTime ?? DateTime.Today).Date;

        try
        {
            var summary = await _database.GetBalanceSheetSummaryAsync(_user.CompanyId, asOfDate);
            _currentSummary = summary;
            var rows = summary.Rows;
            PopulateSection(_assetsRows, rows.Where(x => x.StatementSection == "資産の部"));
            PopulateLiabilitiesAndEquitySection(_liabilitiesRows, rows.Where(x => x.StatementSection != "資産の部"), summary.CurrentPeriodNetIncome);

            var assets = rows.Where(x => x.StatementSection == "資産の部").Sum(x => x.ReportBalance);
            var liabilities = rows.Where(x => x.StatementSection == "負債の部").Sum(x => x.ReportBalance);
            var equityBase = rows.Where(x => x.StatementSection == "資本の部").Sum(x => x.ReportBalance);
            var currentPeriodNetIncome = summary.CurrentPeriodNetIncome;
            var equity = equityBase + currentPeriodNetIncome;
            var liabilitiesAndEquity = liabilities + equity;

            _assetsTotal.Text = FormatFinancialStatementAmount(assets);
            _liabilitiesTotal.Text = FormatFinancialStatementAmount(liabilities);
            _equityTotal.Text = FormatFinancialStatementAmount(equity);
            _currentPeriodNetIncome.Text = FormatFinancialStatementAmount(currentPeriodNetIncome);
            _liabilitiesAndEquityTotal.Text = FormatFinancialStatementAmount(liabilitiesAndEquity);
            SetBalanceCheckMessage(assets, liabilitiesAndEquity);
            _message.Text = $"{asOfDate:yyyy/MM/dd} 現在の貸借対照表を表示しています。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _currentSummary = null;
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
            _balanceCheck.Text = "一致確認を表示できませんでした。";
            _balanceCheck.Foreground = Brush.Parse("#B42318");
        }
    }

    private static DateTime GetLatestClosedDate(DateTime today, int closingDay)
    {
        var normalizedClosingDay = Math.Clamp(closingDay, 1, 31);
        var thisMonthClosing = CreateClosingDate(today.Year, today.Month, normalizedClosingDay);
        return today.Date > thisMonthClosing
            ? thisMonthClosing
            : CreateClosingDate(today.AddMonths(-1).Year, today.AddMonths(-1).Month, normalizedClosingDay);
    }

    private static DateTime CreateClosingDate(int year, int month, int closingDay)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Min(closingDay, lastDay));
    }

    private void PopulateSection(StackPanel container, IEnumerable<BalanceSheetRow> rows)
    {
        container.Children.Clear();
        var groups = rows
            .GroupBy(x => x.ClassificationName)
            .OrderBy(x => x.Min(y => y.ClassificationSortOrder))
            .ToList();

        if (groups.Count == 0)
        {
            container.Children.Add(ViewHelpers.Body("該当する勘定科目がありません。"));
            return;
        }

        foreach (var group in groups)
        {
            container.Children.Add(CategoryHeader(group.Key));

            foreach (var row in group)
            {
                container.Children.Add(AccountRow(row));
            }

            container.Children.Add(CategorySubtotal(group.Key, group.Sum(x => x.ReportBalance)));
        }
    }

    private void PopulateLiabilitiesAndEquitySection(StackPanel container, IEnumerable<BalanceSheetRow> rows, decimal currentPeriodNetIncome)
    {
        container.Children.Clear();

        var rowsBySection = rows
            .GroupBy(x => x.StatementSection)
            .ToDictionary(x => x.Key, x => x.ToList());

        foreach (var sectionName in new[] { "負債の部", "資本の部" })
        {
            container.Children.Add(SectionDivider(sectionName));

            rowsBySection.TryGetValue(sectionName, out var sectionRows);
            sectionRows ??= [];

            var classificationGroups = sectionRows
                .GroupBy(x => x.ClassificationName)
                .OrderBy(x => x.Min(y => y.ClassificationSortOrder))
                .ToList();

            if (classificationGroups.Count == 0)
            {
                container.Children.Add(EmptySectionRow($"{sectionName} に該当する勘定科目がありません。"));
            }

            foreach (var classificationGroup in classificationGroups)
            {
                container.Children.Add(CategoryHeader(classificationGroup.Key));

                foreach (var row in classificationGroup)
                {
                    container.Children.Add(AccountRow(row));
                }

                container.Children.Add(CategorySubtotal(classificationGroup.Key, classificationGroup.Sum(x => x.ReportBalance)));
            }

            var sectionTotal = sectionRows.Sum(x => x.ReportBalance);
            if (sectionName == "資本の部")
            {
                container.Children.Add(AccountSummaryRow("当期純損益", currentPeriodNetIncome));
                sectionTotal += currentPeriodNetIncome;
            }

            container.Children.Add(SectionTotalRow(sectionName, sectionTotal));
        }
    }

    private static Control CreateSectionPanel(string title, Control content)
    {
        return ViewHelpers.Panel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                ViewHelpers.Heading(title, 20),
                TableHeader(),
                content
            }
        });
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
                ColumnDefinitions = new ColumnDefinitions("*,130"),
                Children =
                {
                    HeaderCell("勘定科目", 0),
                    HeaderCell("金額", 1, true)
                }
            }
        };
    }

    private static Control CategoryHeader(string classificationName)
    {
        return new Border
        {
            Background = Brush.Parse("#EEF4FB"),
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 9),
            Child = new TextBlock
            {
                Text = classificationName,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#172033")
            }
        };
    }

    private static Control SectionDivider(string sectionName)
    {
        return new Border
        {
            Background = Brush.Parse("#DCE7F5"),
            BorderBrush = Brush.Parse("#B9C8DB"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(8, 9),
            Child = new TextBlock
            {
                Text = sectionName,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#172033")
            }
        };
    }

    private static Control AccountRow(BalanceSheetRow row)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,130"),
                Children =
                {
                    Cell($"{row.AccountCode} {row.AccountName}", 0, FontWeight.SemiBold),
                    AmountCell(row.ReportBalance, 1)
                }
            }
        };
    }

    private static Control CategorySubtotal(string classificationName, decimal total)
    {
        return new Border
        {
            Background = Brush.Parse("#F8FAFC"),
            BorderBrush = Brush.Parse("#CBD5E1"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,130"),
                Children =
                {
                    Cell($"{classificationName} 小計", 0, FontWeight.SemiBold),
                    AmountCell(total, 1, true)
                }
            }
        };
    }

    private static Control AccountSummaryRow(string label, decimal amount)
    {
        return new Border
        {
            Background = Brush.Parse("#FFF7ED"),
            BorderBrush = Brush.Parse("#F2C897"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,130"),
                Children =
                {
                    Cell(label, 0, FontWeight.SemiBold),
                    AmountCell(amount, 1, true)
                }
            }
        };
    }

    private static Control EmptySectionRow(string text)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brush.Parse("#607086"),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static Control SectionTotalRow(string sectionName, decimal total)
    {
        return new Border
        {
            Background = Brush.Parse("#EAF1E8"),
            BorderBrush = Brush.Parse("#B7C9B3"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,130"),
                Children =
                {
                    Cell($"{sectionName} 合計", 0, FontWeight.SemiBold),
                    AmountCell(total, 1, true)
                }
            }
        };
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

    private static Control AmountCell(decimal amount, int column, bool emphasized = false)
    {
        var block = new TextBlock
        {
            Text = FormatFinancialStatementAmount(amount),
            Foreground = emphasized ? Brush.Parse("#172033") : Brush.Parse("#243044"),
            FontWeight = emphasized ? FontWeight.SemiBold : FontWeight.Normal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static string FormatFinancialStatementAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount).ToString("N0")}"
            : amount.ToString("N0");
    }

    private void SetBalanceCheckMessage(decimal assets, decimal liabilitiesAndEquity)
    {
        var difference = assets - liabilitiesAndEquity;
        if (difference == 0)
        {
            _balanceCheck.Text = "一致確認: 資産 = 負債 + 資本 が一致しています。";
            _balanceCheck.Foreground = Brush.Parse("#1E6B52");
            return;
        }

        _balanceCheck.Text = $"一致確認: 差額 {FormatFinancialStatementAmount(difference)} があります。";
        _balanceCheck.Foreground = Brush.Parse("#B42318");
    }

    private async Task ExportPdfAsync()
    {
        if (_currentSummary is null)
        {
            _message.Text = "出力する貸借対照表がまだ読み込まれていません。";
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

        var asOfDate = (_asOfDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "貸借対照表PDFを保存",
            SuggestedFileName = $"貸借対照表_{asOfDate:yyyyMMdd}.pdf",
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
            await BalanceSheetPdfExporter.ExportAsync(file.Path.LocalPath, _user.CompanyName, asOfDate, _currentSummary);
            var previewError = PdfPreviewLauncher.Open(file.Path.LocalPath);
            _message.Text = previewError ?? $"PDFを書き出しました: {file.Name}";
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
}
