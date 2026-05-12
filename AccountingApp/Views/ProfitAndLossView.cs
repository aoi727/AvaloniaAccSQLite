using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class ProfitAndLossView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly DatePicker _fromDate = new();
    private readonly DatePicker _toDate = new();
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly TextBlock _netSales = ViewHelpers.Body("0");
    private readonly TextBlock _costOfSales = ViewHelpers.Body("0");
    private readonly TextBlock _grossProfit = ViewHelpers.Body("0");
    private readonly TextBlock _sga = ViewHelpers.Body("0");
    private readonly TextBlock _operatingProfit = ViewHelpers.Body("0");
    private readonly TextBlock _otherGains = ViewHelpers.Body("0");
    private readonly TextBlock _otherLosses = ViewHelpers.Body("0");
    private readonly TextBlock _netIncome = ViewHelpers.Body("0");
    private readonly TextBlock _message = ViewHelpers.Body("損益計算書を読み込み中です。");
    private readonly Button _exportPdfButton = ViewHelpers.SecondaryButton("PDF出力");
    private ProfitAndLossSummary? _currentSummary;
    private bool _isInitializing;
    private bool _isAdjustingDateRange;

    public ProfitAndLossView(SqliteDatabase database, AppUser user, Action backToDashboard)
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
        refreshButton.Click += async (_, _) => await LoadProfitAndLossAsync();

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
                        ViewHelpers.Body("損益計算書")
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
                WithMargin(Field("開始日", _fromDate), new Thickness(0, 0, 16, 12)),
                WithMargin(Field("終了日", _toDate), new Thickness(0, 0, 16, 12)),
                ButtonField(refreshButton)
            }
        };

        var summaryPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,16,Auto,120,16,Auto,120,16,Auto,120"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SummaryLabel("売上高", 0),
                SummaryBox(_netSales, 1),
                SummaryLabel("売上原価", 3),
                SummaryBox(_costOfSales, 4),
                SummaryLabel("売上総利益", 6),
                SummaryBox(_grossProfit, 7),
                SummaryLabel("営業利益", 9),
                SummaryBox(_operatingProfit, 10)
            }
        };

        var subSummaryPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,16,Auto,120,16,Auto,120,16,Auto,120"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SummaryLabel("販管費", 0),
                SummaryBox(_sga, 1),
                SummaryLabel("営業外等収益", 3),
                SummaryBox(_otherGains, 4),
                SummaryLabel("営業外等費用", 6),
                SummaryBox(_otherLosses, 7),
                SummaryLabel("当期純損益", 9),
                SummaryBox(_netIncome, 10)
            }
        };

        var controls = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                filterRow,
                summaryPanel,
                subSummaryPanel
            }
        });

        var body = ViewHelpers.Panel(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Children =
            {
                TableHeader(),
                _message,
                new ScrollViewer { Content = _rows }
            }
        });
        Grid.SetRow(_message, 1);
        Grid.SetRow(((Grid)body.Child!).Children[2], 2);

        var layout = new Grid
        {
            Margin = new Thickness(28),
            RowDefinitions = new RowDefinitions("Auto,18,Auto,18,*"),
            Children =
            {
                header,
                controls,
                body
            }
        };
        Grid.SetRow(controls, 2);
        Grid.SetRow(body, 4);
        return layout;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _isInitializing = true;
            var settings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            var closingDay = settings.ClosingDay;
            var latestClosed = GetLatestClosedDate(DateTime.Today, closingDay);
            var fiscalYearStart = GetFiscalYearStartDate(settings.FiscalYearStart, latestClosed);
            _fromDate.SelectedDate = new DateTimeOffset(fiscalYearStart);
            _toDate.SelectedDate = new DateTimeOffset(latestClosed);
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

        await LoadProfitAndLossAsync();
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

        await LoadProfitAndLossAsync();
    }

    private async Task HandleDateChangedAsync()
    {
        if (_isInitializing || _isAdjustingDateRange)
        {
            return;
        }

        await LoadProfitAndLossAsync();
    }

    private async Task LoadProfitAndLossAsync()
    {
        var fromDate = (_fromDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
        var toDate = (_toDate.SelectedDate?.DateTime ?? DateTime.Today).Date;

        try
        {
            var summary = await _database.GetProfitAndLossSummaryAsync(_user.CompanyId, fromDate, toDate);
            _currentSummary = summary;
            PopulateRows(summary.Rows);
            _netSales.Text = FormatFinancialStatementAmount(summary.NetSales);
            _costOfSales.Text = FormatFinancialStatementAmount(summary.CostOfSales);
            _grossProfit.Text = FormatFinancialStatementAmount(summary.GrossProfit);
            _sga.Text = FormatFinancialStatementAmount(summary.SellingGeneralAdministrativeExpenses);
            _operatingProfit.Text = FormatFinancialStatementAmount(summary.OperatingProfit);
            _otherGains.Text = FormatFinancialStatementAmount(summary.NonOperatingAndSpecialGains);
            _otherLosses.Text = FormatFinancialStatementAmount(summary.NonOperatingAndSpecialLosses);
            _netIncome.Text = FormatFinancialStatementAmount(summary.NetIncome);
            _message.Text = $"{fromDate:yyyy/MM/dd} から {toDate:yyyy/MM/dd} までの損益計算書を表示しています。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _currentSummary = null;
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private void PopulateRows(IEnumerable<ProfitAndLossRow> rows)
    {
        _rows.Children.Clear();
        var sectionGroups = rows
            .GroupBy(x => x.StatementSection)
            .OrderBy(x => x.Min(y => y.ClassificationSortOrder))
            .ToList();

        if (sectionGroups.Count == 0)
        {
            _rows.Children.Add(ViewHelpers.Body("該当する損益データがありません。"));
            return;
        }

        foreach (var sectionGroup in sectionGroups)
        {
            _rows.Children.Add(SectionDivider(sectionGroup.Key));

            var classificationGroups = sectionGroup
                .GroupBy(x => x.ClassificationName)
                .OrderBy(x => x.Min(y => y.ClassificationSortOrder))
                .ToList();

            foreach (var classificationGroup in classificationGroups)
            {
                _rows.Children.Add(CategoryHeader(classificationGroup.Key));

                foreach (var row in classificationGroup)
                {
                    _rows.Children.Add(AccountRow(row));
                }

                _rows.Children.Add(CategorySubtotal(classificationGroup.Key, classificationGroup.Sum(x => x.ReportAmount)));
            }

            _rows.Children.Add(SectionSubtotal(sectionGroup.Key, sectionGroup.Sum(x => x.ReportAmount)));
        }
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

    private static Control AccountRow(ProfitAndLossRow row)
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
                    AmountCell(row.ReportAmount, 1)
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

    private static Control SectionSubtotal(string sectionName, decimal total)
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
                    Cell($"{sectionName} 合計", 0, FontWeight.SemiBold),
                    AmountCell(total, 1, true)
                }
            }
        };
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

    private static DateTime GetFiscalYearStartDate(DateTime template, DateTime targetDate)
    {
        var year = targetDate.Month >= template.Month ? targetDate.Year : targetDate.Year - 1;
        var day = Math.Min(template.Day, DateTime.DaysInMonth(year, template.Month));
        return new DateTime(year, template.Month, day);
    }

    private static string FormatFinancialStatementAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount).ToString("N0")}"
            : amount.ToString("N0");
    }

    private async Task ExportPdfAsync()
    {
        if (_currentSummary is null)
        {
            _message.Text = "出力する損益計算書がまだ読み込まれていません。";
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

        var fromDate = (_fromDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
        var toDate = (_toDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "損益計算書PDFを保存",
            SuggestedFileName = $"損益計算書_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf",
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
            await ProfitAndLossPdfExporter.ExportAsync(file.Path.LocalPath, _user.CompanyName, fromDate, toDate, _currentSummary);
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
