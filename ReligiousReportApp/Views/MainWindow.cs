using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ReligiousReportApp.Data;
using ReligiousReportApp.Models;

namespace ReligiousReportApp.Views;

public sealed class MainWindow : Window
{
    private sealed record Option<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RoleControl(AccountRoleRow Source, ComboBox Role, ComboBox Category);
    private sealed record ReviewControl(CashFlowReviewRow Source, ComboBox Treatment, ComboBox Category, TextBox Note);
    private sealed record BudgetControl(long CategoryId, TextBox Amount);

    private static readonly Option<string>[] RoleOptions =
    [
        new("cash", "現金・預金"),
        new("income", "通常収入"),
        new("expense", "通常支出"),
        new("borrowing", "借入金"),
        new("deposit", "預り金・対象外"),
        new("payable", "未払金・要確認"),
        new("receivable", "未収入金・要確認"),
        new("excluded", "対象外"),
        new("manual", "毎回確認")
    ];

    private static readonly Option<string>[] TreatmentOptions =
    [
        new("include", "報告書に載せる"),
        new("exclude", "対象外"),
        new("manual", "要確認")
    ];

    private readonly TextBox _databasePath = new() { PlaceholderText = "AccountingApp の SQLite DB ファイル" };
    private readonly TextBlock _companyLabel = ViewHelpers.Body("DBを選択してください。");
    private readonly TextBlock _message = ViewHelpers.Body("任意期間の仮出力と年度報告書の下準備を行います。");
    private readonly DatePicker _fiscalYearStart = new();
    private readonly DatePicker _periodStart = new();
    private readonly DatePicker _periodEnd = new();
    private readonly StackPanel _categoryRows = new() { Spacing = 0 };
    private readonly StackPanel _roleRows = new() { Spacing = 0 };
    private readonly StackPanel _reviewRows = new() { Spacing = 0 };
    private readonly StackPanel _budgetRows = new() { Spacing = 0 };
    private readonly StackPanel _reportRows = new() { Spacing = 0 };
    private readonly TextBlock _reviewStatus = ViewHelpers.Body("");
    private readonly TextBlock _reportStatus = ViewHelpers.Body("");
    private readonly TextBox _categoryCode = new() { PlaceholderText = "例 I010" };
    private readonly TextBox _categoryName = new() { PlaceholderText = "分類名" };
    private readonly ComboBox _categoryKind = new();
    private readonly TextBox _categoryOrder = new() { Text = "10" };
    private readonly TextBox _openingCarryover = new() { PlaceholderText = "0", HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly TextBox _reportNote = new() { AcceptsReturn = true, MinHeight = 72, TextWrapping = TextWrapping.Wrap };
    private bool _loadingReportNote;
    private readonly List<RoleControl> _roleControls = [];
    private readonly List<ReviewControl> _reviewControls = [];
    private readonly List<BudgetControl> _budgetControls = [];
    private AccountingDatabase? _database;
    private CompanyInfo? _company;
    private ReligiousReportSummary? _currentSummary;
    private bool _loading;

    public MainWindow()
    {
        Title = "宗教法人 運営収支報告書";
        Width = 1480;
        Height = 860;
        MinWidth = 1120;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _categoryKind.ItemsSource = new[] { new Option<string>("income", "収入"), new Option<string>("expense", "支出") };
        _categoryKind.SelectedIndex = 0;
        _fiscalYearStart.SelectedDateChanged += async (_, _) => await ReloadBudgetsAndReportAsync();
        _periodStart.SelectedDateChanged += async (_, _) => await ReloadPeriodAsync();
        _periodEnd.SelectedDateChanged += async (_, _) => await ReloadPeriodAsync();
        Content = Build();
    }

    private Control Build()
    {
        var openButton = ViewHelpers.PrimaryButton("DBを開く");
        openButton.Width = 100;
        openButton.Click += async (_, _) => await OpenDatabaseAsync();

        var reloadButton = ViewHelpers.SecondaryButton("再読み込み");
        reloadButton.Width = 110;
        reloadButton.Click += async (_, _) => await ReloadAllAsync();

        var header = ViewHelpers.Panel(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,Auto,12,Auto"),
            Children =
            {
                new StackPanel { Spacing = 6, Children = { ViewHelpers.Heading("運営収支報告書"), _companyLabel, _databasePath } },
                openButton,
                reloadButton
            }
        });
        Grid.SetColumn(openButton, 2);
        Grid.SetColumn(reloadButton, 4);

        var tab = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "収支分類", Content = CategoryTab() },
                new TabItem { Header = "科目設定", Content = RoleTab() },
                new TabItem { Header = "入出金レビュー", Content = ReviewTab() },
                new TabItem { Header = "年度予算", Content = BudgetTab() },
                new TabItem { Header = "報告書プレビュー", Content = ReportTab() }
            }
        };

        return new Border
        {
            Background = Brush.Parse("#F3F6FB"),
            Padding = new Thickness(18),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,14,*,12,Auto"),
                Children = { header, tab, MessageBar() }
            }.Also(grid =>
            {
                Grid.SetRow(tab, 2);
                Grid.SetRow(grid.Children[2], 4);
            })
        };
    }

    private Control CategoryTab()
    {
        var saveButton = ViewHelpers.PrimaryButton("分類を追加");
        saveButton.Click += async (_, _) => await SaveCategoryAsync();
        var form = ViewHelpers.Panel(new WrapPanel
        {
            Children =
            {
                WithMargin(Field("分類コード", _categoryCode, 120), new Thickness(0, 0, 12, 10)),
                WithMargin(Field("分類名", _categoryName, 220), new Thickness(0, 0, 12, 10)),
                WithMargin(Field("区分", _categoryKind, 120), new Thickness(0, 0, 12, 10)),
                WithMargin(Field("表示順", _categoryOrder, 100), new Thickness(0, 0, 12, 10)),
                ButtonField(saveButton)
            }
        });
        return TabGrid(form, Header(new ColumnDefinitions("110,90,*,100"), ["コード", "区分", "分類名", "表示順"]), _categoryRows);
    }

    private Control RoleTab()
    {
        var saveButton = ViewHelpers.PrimaryButton("科目設定を保存");
        saveButton.Click += async (_, _) => await SaveRolesAsync();
        var top = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                ViewHelpers.Body("現金・預金科目を起点に、相手科目から収支報告書への表示可否と分類を判定します。"),
                saveButton
            }
        });
        return TabGrid(top, Header(new ColumnDefinitions("100,120,*,190,260"), ["コード", "科目区分", "勘定科目", "役割", "標準分類"]), _roleRows);
    }

    private Control ReviewTab()
    {
        var loadButton = ViewHelpers.PrimaryButton("期間を表示");
        loadButton.Click += async (_, _) => await LoadReviewAsync();
        var saveButton = ViewHelpers.SecondaryButton("レビューを保存");
        saveButton.Click += async (_, _) => await SaveReviewAsync();
        var reviewedButton = ViewHelpers.SecondaryButton("確認済みにする");
        reviewedButton.Click += async (_, _) => await MarkReviewedAsync();
        var finalizeButton = ViewHelpers.SecondaryButton("確定する");
        finalizeButton.Click += async (_, _) => await FinalizePeriodAsync();
        var unfinalizeButton = ViewHelpers.SecondaryButton("確定解除");
        unfinalizeButton.Click += async (_, _) => await UnfinalizePeriodAsync();
        var top = ViewHelpers.Panel(new WrapPanel
        {
            Children =
            {
                WithMargin(Field("開始日", _periodStart, 180), new Thickness(0, 0, 12, 10)),
                WithMargin(Field("終了日", _periodEnd, 180), new Thickness(0, 0, 12, 10)),
                ButtonField(loadButton),
                WithMargin(ButtonField(saveButton), new Thickness(12, 0, 0, 0)),
                WithMargin(ButtonField(reviewedButton), new Thickness(12, 0, 0, 0)),
                WithMargin(ButtonField(finalizeButton), new Thickness(12, 0, 0, 0)),
                WithMargin(ButtonField(unfinalizeButton), new Thickness(12, 0, 0, 0)),
                WithMargin(_reviewStatus, new Thickness(18, 30, 0, 0))
            }
        });
        return TabGrid(top, Header(new ColumnDefinitions("95,95,*,80,120,180,220,150,230,140"), ["日付", "番号", "摘要", "入出金", "金額", "現預金", "相手科目", "判定", "分類", "メモ"]), _reviewRows);
    }

    private Control BudgetTab()
    {
        var saveButton = ViewHelpers.PrimaryButton("予算を保存");
        saveButton.Click += async (_, _) => await SaveBudgetsAsync();
        var top = ViewHelpers.Panel(new WrapPanel
        {
            Children =
            {
                WithMargin(Field("年度開始日", _fiscalYearStart, 220), new Thickness(0, 0, 12, 10)),
                ButtonField(saveButton)
            }
        });
        return TabGrid(top, Header(new ColumnDefinitions("110,90,*,160"), ["コード", "区分", "分類名", "年額予算"]), _budgetRows);
    }

    private Control ReportTab()
    {
        var refreshButton = ViewHelpers.PrimaryButton("プレビュー更新");
        refreshButton.Click += async (_, _) => await LoadReportAsync();
        var saveCarryoverButton = ViewHelpers.SecondaryButton("繰越を保存");
        saveCarryoverButton.Click += async (_, _) => await SaveCarryoverAsync();
        var generateCarryoverButton = ViewHelpers.SecondaryButton("前年から作成");
        generateCarryoverButton.Click += async (_, _) => await GenerateCarryoverAsync();
        var saveNoteButton = ViewHelpers.SecondaryButton("注記を保存");
        saveNoteButton.Click += async (_, _) => await SaveReportNoteAsync();
        var exportPdfButton = ViewHelpers.SecondaryButton("PDF出力");
        exportPdfButton.Click += async (_, _) => await ExportPdfAsync(exportPdfButton);
        var top = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                ViewHelpers.Body("入出金レビュー済みの内容をもとに、任意期間の運営収支報告書を仮出力します。"),
                new WrapPanel
                {
                    Children =
                    {
                        WithMargin(Field("期首繰越収支差額", _openingCarryover, 190), new Thickness(0, 0, 12, 10)),
                        ButtonField(saveCarryoverButton),
                        WithMargin(ButtonField(generateCarryoverButton), new Thickness(12, 0, 0, 0)),
                        WithMargin(ButtonField(refreshButton), new Thickness(12, 0, 0, 0)),
                        WithMargin(ButtonField(exportPdfButton), new Thickness(12, 0, 0, 0)),
                        WithMargin(_reportStatus, new Thickness(14, 30, 0, 0))
                    }
                },
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,12,Auto"),
                    Children = { Field("注記", _reportNote, 420), saveNoteButton }
                }.Also(grid =>
                {
                    Grid.SetColumn(saveNoteButton, 2);
                    saveNoteButton.VerticalAlignment = VerticalAlignment.Bottom;
                })
            }
        });
        return TabGrid(top, Header(new ColumnDefinitions("120,*,150,150,150"), ["コード", "分類", "期間予算", "実績額", "差異"]), _reportRows);
    }

    private static Grid TabGrid(Control top, Control header, StackPanel rows)
    {
        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,12,Auto,*"),
            Children = { top, header, new ScrollViewer { Content = rows } }
        }.Also(grid =>
        {
            Grid.SetRow(header, 2);
            Grid.SetRow(grid.Children[2], 3);
        });
    }

    private Border MessageBar()
    {
        return new Border
        {
            Background = Brush.Parse("#EEF4FB"),
            BorderBrush = Brush.Parse("#D8E4F2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8),
            Child = _message
        };
    }

    private async Task OpenDatabaseAsync()
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            SetError("ファイル選択を開けませんでした。");
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "AccountingApp SQLite DBを選択",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SQLite DB") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] },
                FilePickerFileTypes.All
            ]
        });
        var path = files.Count > 0 ? files[0].Path.LocalPath : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            _databasePath.Text = path;
            await LoadDatabaseAsync(path);
        }
    }

    private async Task LoadDatabaseAsync(string path)
    {
        try
        {
            _loading = true;
            _database = new AccountingDatabase(path);
            await _database.InitializeReligiousReportSchemaAsync();
            _company = await _database.GetCompanyAsync();
            var fiscalStart = _database.GetFiscalYearStartFor(_company, DateTime.Today);
            _fiscalYearStart.SelectedDate = new DateTimeOffset(fiscalStart);
            var firstDay = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _periodStart.SelectedDate = new DateTimeOffset(firstDay);
            _periodEnd.SelectedDate = new DateTimeOffset(firstDay.AddMonths(1).AddDays(-1));
            _companyLabel.Text = $"{_company.Name} / AccountingApp DBを参照中";
            _loading = false;
            await ReloadAllAsync();
            SetMessage("DBを開きました。AccountingApp本体のテーブルは変更せず、religious_report_* テーブルだけを使います。", false);
        }
        catch (Exception ex)
        {
            _loading = false;
            SetError(ex.Message);
        }
    }

    private async Task ReloadAllAsync()
    {
        if (_database is null || _company is null)
        {
            SetError("先にDBを開いてください。");
            return;
        }

        await LoadCategoriesAsync();
        await LoadRolesAsync();
        await ReloadBudgetsAndReportAsync();
        await ReloadPeriodAsync();
    }

    private async Task ReloadBudgetsAndReportAsync()
    {
        if (_loading || _database is null || _company is null) return;
        await LoadBudgetsAsync();
        await LoadCarryoverAsync();
        await LoadReportAsync();
    }

    private async Task ReloadPeriodAsync()
    {
        if (_loading || _database is null || _company is null) return;
        await LoadReviewAsync();
        await LoadReportAsync();
    }

    private async Task SaveCategoryAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        try
        {
            var kind = _categoryKind.SelectedItem is Option<string> option ? option.Value : "income";
            await _database.SaveCategoryAsync(new ReligiousReportCategory(null, _company.CompanyId, _categoryCode.Text ?? "", _categoryName.Text ?? "", kind, ParseInt(_categoryOrder.Text, "表示順"), true));
            _categoryCode.Text = "";
            _categoryName.Text = "";
            await ReloadAllAsync();
            SetMessage("収支分類を保存しました。", false);
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async Task LoadCategoriesAsync()
    {
        _categoryRows.Children.Clear();
        if (_database is null || _company is null) return;
        foreach (var category in await _database.GetCategoriesAsync(_company.CompanyId))
        {
            _categoryRows.Children.Add(RowBorder(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("110,90,*,100"),
                Children =
                {
                    Cell(category.Code, 0, FontWeight.SemiBold),
                    Cell(category.Kind == "income" ? "収入" : "支出", 1),
                    Cell(category.Name, 2),
                    Cell(category.DisplayOrder.ToString("N0"), 3, default, true)
                }
            }));
        }
    }

    private async Task LoadRolesAsync()
    {
        _roleRows.Children.Clear();
        _roleControls.Clear();
        if (_database is null || _company is null) return;
        var categoryOptions = await GetCategoryOptionsAsync(includeBlank: true);
        foreach (var row in await _database.GetAccountRolesAsync(_company.CompanyId))
        {
            var role = Combo(RoleOptions, x => x.Value == row.Role);
            var category = Combo(categoryOptions, x => x.Value == row.DefaultCategoryId);
            _roleControls.Add(new RoleControl(row, role, category));
            _roleRows.Children.Add(RowBorder(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("100,120,*,190,260"),
                Children =
                {
                    Cell(row.AccountCode, 0, FontWeight.SemiBold),
                    Cell(AccountTypeLabel(row.AccountType), 1),
                    Cell(row.AccountName, 2),
                    Place(role, 3),
                    Place(category, 4)
                }
            }));
        }
    }

    private async Task SaveRolesAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        var rows = _roleControls.Select(x => x.Source with
        {
            Role = SelectedValue(x.Role, "excluded"),
            DefaultCategoryId = SelectedValue<long?>(x.Category, null)
        }).ToList();
        await _database.SaveAccountRolesAsync(_company.CompanyId, rows);
        await LoadReviewAsync();
        await LoadReportAsync();
        SetMessage("科目設定を保存しました。", false);
    }

    private async Task LoadReviewAsync()
    {
        _reviewRows.Children.Clear();
        _reviewControls.Clear();
        _reviewStatus.Text = "";
        if (_database is null || _company is null) return;
        var (start, end) = GetPeriod();
        var categoryOptions = await GetCategoryOptionsAsync(includeBlank: true);
        var rows = await _database.GetCashFlowReviewRowsAsync(_company.CompanyId, start, end);
        var status = await _database.GetPeriodReviewStatusAsync(_company.CompanyId, start, end);
        var changed = rows.Count(x => x.IsChanged);
        var unresolved = rows.Count(x => x.IsChanged || x.EffectiveTreatment == "manual" || (x.EffectiveTreatment == "include" && !x.EffectiveCategoryId.HasValue));
        var composite = rows.Count(x => x.IsComposite);
        _reviewStatus.Text = $"{rows.Count:N0}明細 / 複合内訳 {composite:N0}明細 / 変更あり {changed:N0}件 / 要確認 {unresolved:N0}件 / 状態 {StatusLabel(status?.Status ?? "draft")}";

        foreach (var row in rows)
        {
            var treatment = Combo(TreatmentOptions, x => x.Value == row.EffectiveTreatment);
            var category = Combo(categoryOptions, x => x.Value == row.EffectiveCategoryId);
            var note = new TextBox { Text = row.Note ?? "", MinWidth = 130 };
            _reviewControls.Add(new ReviewControl(row, treatment, category, note));
            _reviewRows.Children.Add(RowBorder(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("95,95,*,80,120,180,220,150,230,140"),
                Children =
                {
                    Cell(row.EntryDate.ToString("yyyy/MM/dd"), 0),
                    Cell(row.EntryNumber, 1),
                    Cell(BuildReviewDescription(row), 2, row.IsChanged ? FontWeight.SemiBold : FontWeight.Normal),
                    Cell(row.Direction == "income" ? "入金" : "出金", 3),
                    AmountCell(row.Amount, 4),
                    Cell(row.CashAccountDisplay, 5),
                    Cell(row.CounterAccountDisplay, 6),
                    Place(treatment, 7),
                    Place(category, 8),
                    Place(note, 9)
                }
            }));
        }
    }

    private async Task SaveReviewAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        if (await IsCurrentPeriodFinalizedAsync())
        {
            SetError("この期間は確定済みです。レビュー結果は変更できません。");
            return;
        }

        var rows = _reviewControls.Select(x => new CashFlowOverrideInput(
            x.Source.CashLineId,
            x.Source.CounterLineId,
            x.Source.SourceHash,
            x.Source.SourceSnapshot,
            SelectedValue(x.Treatment, "manual"),
            SelectedValue<long?>(x.Category, null),
            x.Note.Text)).ToList();
        await _database.SaveCashFlowOverridesAsync(_company.CompanyId, rows);
        await LoadReviewAsync();
        await LoadReportAsync();
        SetMessage("入出金レビューを保存しました。", false);
    }

    private async Task MarkReviewedAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        if (await IsCurrentPeriodFinalizedAsync())
        {
            SetError("この期間は確定済みです。確認済みへ戻す操作はできません。");
            return;
        }

        await SaveReviewAsync();
        var (start, end) = GetPeriod();
        await _database.SavePeriodReviewStatusAsync(_company.CompanyId, start, end, "reviewed");
        await LoadReviewAsync();
        SetMessage("この期間を確認済みにしました。", false);
    }

    private async Task FinalizePeriodAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        if (await IsCurrentPeriodFinalizedAsync())
        {
            SetError("この期間はすでに確定済みです。");
            return;
        }

        await SaveReviewAsync();
        var (start, end) = GetPeriod();
        var rows = await _database.GetCashFlowReviewRowsAsync(_company.CompanyId, start, end);
        var unresolved = rows.Count(x => x.IsChanged || x.EffectiveTreatment == "manual" || (x.EffectiveTreatment == "include" && !x.EffectiveCategoryId.HasValue));
        if (unresolved > 0)
        {
            SetError($"要確認または変更ありの明細が {unresolved:N0} 件あるため、確定できません。");
            return;
        }

        await _database.SavePeriodReviewStatusAsync(_company.CompanyId, start, end, "finalized");
        await LoadReviewAsync();
        await LoadReportAsync();
        SetMessage("この期間を確定しました。以後、レビュー結果の変更はできません。", false);
    }

    private async Task UnfinalizePeriodAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }

        var (start, end) = GetPeriod();
        var status = await _database.GetPeriodReviewStatusAsync(_company.CompanyId, start, end);
        if (status?.Status != "finalized")
        {
            SetError("この期間は確定済みではありません。");
            return;
        }

        if (!await ConfirmUnfinalizeAsync(start, end))
        {
            SetMessage("確定解除を中止しました。", false);
            return;
        }

        var changed = await _database.UnfinalizePeriodReviewAsync(_company.CompanyId, start, end);
        if (!changed)
        {
            SetError("確定解除できませんでした。期間の状態を再読み込みしてください。");
            return;
        }

        await LoadReviewAsync();
        await LoadReportAsync();
        SetMessage("この期間の確定を解除し、確認済みに戻しました。レビュー結果を変更できます。", false);
    }

    private async Task<bool> ConfirmUnfinalizeAsync(DateTime start, DateTime end)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            SetError("確認ダイアログを表示できませんでした。");
            return false;
        }

        var cancelButton = ViewHelpers.SecondaryButton("キャンセル");
        var okButton = ViewHelpers.PrimaryButton("確定解除する");
        var dialog = new Window
        {
            Title = "確定解除の確認",
            Width = 430,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(22),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        ViewHelpers.Heading("確定を解除しますか", 18),
                        new TextBlock
                        {
                            Text = $"{start:yyyy/MM/dd} - {end:yyyy/MM/dd} の期間を確認済みに戻します。レビュー結果の編集が再び可能になります。",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = Brush.Parse("#243044")
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 10,
                            Children = { cancelButton, okButton }
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        okButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(owner);
    }

    private async Task<bool> IsCurrentPeriodFinalizedAsync()
    {
        if (_database is null || _company is null) return false;
        var (start, end) = GetPeriod();
        var status = await _database.GetPeriodReviewStatusAsync(_company.CompanyId, start, end);
        return status?.Status == "finalized";
    }

    private async Task LoadBudgetsAsync()
    {
        _budgetRows.Children.Clear();
        _budgetControls.Clear();
        if (_database is null || _company is null) return;
        var fiscalStart = GetFiscalStart();
        var budgets = await _database.GetBudgetsAsync(_company.CompanyId, fiscalStart);
        foreach (var category in await _database.GetCategoriesAsync(_company.CompanyId))
        {
            var amount = category.CategoryId.HasValue && budgets.TryGetValue(category.CategoryId.Value, out var value) ? value : 0;
            var input = new TextBox { Text = amount == 0 ? "" : amount.ToString("N0"), HorizontalContentAlignment = HorizontalAlignment.Right, MinWidth = 140 };
            _budgetControls.Add(new BudgetControl(category.CategoryId!.Value, input));
            _budgetRows.Children.Add(RowBorder(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("110,90,*,160"),
                Children =
                {
                    Cell(category.Code, 0, FontWeight.SemiBold),
                    Cell(category.Kind == "income" ? "収入" : "支出", 1),
                    Cell(category.Name, 2),
                    Place(input, 3)
                }
            }));
        }
    }

    private async Task LoadCarryoverAsync()
    {
        if (_database is null || _company is null) return;
        var amount = await _database.GetOpeningCarryoverAsync(_company.CompanyId, GetFiscalStart());
        _openingCarryover.Text = amount == 0 ? "" : amount.ToString("N0");
    }

    private async Task SaveCarryoverAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        try
        {
            var amount = ParseSignedAmount(_openingCarryover.Text, "期首繰越収支差額");
            await _database.SaveOpeningCarryoverAsync(_company.CompanyId, GetFiscalStart(), amount);
            await LoadReportAsync();
            SetMessage("期首繰越収支差額を保存しました。", false);
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async Task GenerateCarryoverAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }

        var fiscalStart = GetFiscalStart();
        var previousStart = fiscalStart.AddYears(-1);
        var previousEnd = fiscalStart.AddDays(-1);
        var existing = await _database.GetOpeningCarryoverAsync(_company.CompanyId, fiscalStart);
        if (!await ConfirmGenerateCarryoverAsync(fiscalStart, previousStart, previousEnd, existing))
        {
            SetMessage("前年からの期首繰越作成を中止しました。", false);
            return;
        }

        try
        {
            var result = await _database.GenerateOpeningCarryoverFromPreviousFinalizedYearAsync(_company.CompanyId, fiscalStart);
            _openingCarryover.Text = result.GeneratedOpeningCarryover == 0 ? "" : result.GeneratedOpeningCarryover.ToString("N0");
            await LoadReportAsync();
            SetMessage(
                $"前年確定データから期首繰越を作成しました。前年期首 {FormatAmount(result.PreviousOpeningCarryover)} + 前年収支 {FormatAmount(result.PreviousNetActual)} = {FormatAmount(result.GeneratedOpeningCarryover)}",
                false);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private async Task<bool> ConfirmGenerateCarryoverAsync(DateTime fiscalStart, DateTime previousStart, DateTime previousEnd, decimal existing)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            SetError("確認ダイアログを表示できませんでした。");
            return false;
        }

        var cancelButton = ViewHelpers.SecondaryButton("キャンセル");
        var okButton = ViewHelpers.PrimaryButton("作成する");
        var existingText = existing == 0
            ? ""
            : $"現在の期首繰越 {FormatAmount(existing)} は、作成結果で上書きされます。";
        var dialog = new Window
        {
            Title = "期首繰越の自動作成",
            Width = 470,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(22),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        ViewHelpers.Heading("前年確定データから作成しますか", 18),
                        new TextBlock
                        {
                            Text = $"{previousStart:yyyy/MM/dd} - {previousEnd:yyyy/MM/dd} の確定済み報告書から、{fiscalStart:yyyy/MM/dd} 開始年度の期首繰越収支差額を作成します。\n{existingText}".Trim(),
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = Brush.Parse("#243044")
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 10,
                            Children = { cancelButton, okButton }
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        okButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(owner);
    }

    private async Task LoadReportNoteAsync()
    {
        if (_database is null || _company is null || _loadingReportNote) return;
        var (start, end) = GetPeriod();
        _loadingReportNote = true;
        _reportNote.Text = await _database.GetReportNoteAsync(_company.CompanyId, start, end);
        _loadingReportNote = false;
    }

    private async Task SaveReportNoteAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        var (start, end) = GetPeriod();
        await _database.SaveReportNoteAsync(_company.CompanyId, start, end, _reportNote.Text);
        await LoadReportAsync();
        SetMessage("注記を保存しました。", false);
    }

    private async Task SaveBudgetsAsync()
    {
        if (_database is null || _company is null) { SetError("先にDBを開いてください。"); return; }
        try
        {
            var budgets = _budgetControls.ToDictionary(x => x.CategoryId, x => ParseAmount(x.Amount.Text, "予算額"));
            await _database.SaveBudgetsAsync(_company.CompanyId, GetFiscalStart(), budgets);
            await LoadReportAsync();
            SetMessage("年度予算を保存しました。", false);
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private async Task LoadReportAsync()
    {
        _reportRows.Children.Clear();
        _reportStatus.Text = "";
        _currentSummary = null;
        if (_database is null || _company is null) return;
        var (start, end) = GetPeriod();
        var summary = await _database.GetReportSummaryAsync(_company.CompanyId, start, end);
        _currentSummary = summary;
        await LoadReportNoteAsync();
        _reportStatus.Text = $"{summary.PeriodStart:yyyy/MM/dd} - {summary.PeriodEnd:yyyy/MM/dd} / 予算按分 {summary.BudgetFactor:P0} / 要確認・変更あり {summary.UnresolvedCount:N0}件";
        _reportRows.Children.Add(ReportTitleBlock(summary));
        _reportRows.Children.Add(ReportCarryoverRow("前期繰越収支差額", summary.PeriodOpeningCarryover));
        _reportRows.Children.Add(SectionRow("収入の部"));
        _reportRows.Children.Add(ReportSummaryRow("収入合計", summary.IncomeBudgetTotal, summary.IncomeActualTotal, summary.IncomeBudgetTotal - summary.IncomeActualTotal));
        foreach (var row in summary.Rows.Where(x => x.Kind == "income")) _reportRows.Children.Add(ReportDetailRow(row));
        _reportRows.Children.Add(SectionRow("支出の部"));
        _reportRows.Children.Add(ReportSummaryRow("支出合計", summary.ExpenseBudgetTotal, summary.ExpenseActualTotal, summary.ExpenseBudgetTotal - summary.ExpenseActualTotal));
        foreach (var row in summary.Rows.Where(x => x.Kind == "expense")) _reportRows.Children.Add(ReportDetailRow(row));
        _reportRows.Children.Add(ReportSummaryRow("当期収支差額", summary.NetBudget, summary.NetActual, summary.NetBudget - summary.NetActual, Brush.Parse("#FFF7ED")));
        _reportRows.Children.Add(ReportCarryoverRow("次期繰越収支差額", summary.ClosingCarryover, Brush.Parse("#ECFDF3")));
        _reportRows.Children.Add(ReportNoteBlock());
    }

    private async Task ExportPdfAsync(Button exportButton)
    {
        if (_database is null || _company is null)
        {
            SetError("先にDBを開いてください。");
            return;
        }

        if (_currentSummary is null)
        {
            await LoadReportAsync();
        }

        if (_currentSummary is null)
        {
            SetError("出力する運営収支報告書がまだ読み込まれていません。");
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            SetError("保存ダイアログを開けませんでした。");
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "運営収支報告書PDFを保存",
            SuggestedFileName = $"運営収支報告書_{_currentSummary.PeriodStart:yyyyMMdd}_{_currentSummary.PeriodEnd:yyyyMMdd}.pdf",
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
            exportButton.IsEnabled = false;
            await ReligiousReportPdfExporter.ExportAsync(
                file.Path.LocalPath,
                _company.Name,
                _currentSummary,
                _reportNote.Text ?? "");
            var previewError = PdfPreviewLauncher.Open(file.Path.LocalPath);
            SetMessage(previewError ?? $"PDFを書き出しました: {file.Name}", previewError is not null);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            exportButton.IsEnabled = true;
        }
    }

    private Control ReportDetailRow(ReligiousReportRow row)
    {
        return RowBorder(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*,150,150,150"),
            Children = { Cell(row.CategoryCode, 0, FontWeight.SemiBold), Cell(row.CategoryName, 1), AmountCell(row.BudgetAmount, 2), AmountCell(row.ActualAmount, 3), AmountCell(row.VarianceAmount, 4) }
        });
    }

    private Control ReportTitleBlock(ReligiousReportSummary summary)
    {
        var title = new TextBlock
        {
            Text = "運営収支報告書",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#172033"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var period = new TextBlock
        {
            Text = $"自 {summary.PeriodStart:yyyy年M月d日}  至 {summary.PeriodEnd:yyyy年M月d日}",
            FontSize = 14,
            Foreground = Brush.Parse("#243044"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var company = new TextBlock
        {
            Text = _company?.Name ?? "",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#243044"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#CBD5E1"),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Padding = new Thickness(18, 16),
            Child = new StackPanel { Spacing = 6, Children = { title, period, company } }
        };
    }

    private static Control SectionRow(string label)
    {
        return new Border
        {
            Background = Brush.Parse("#E6E9ED"),
            BorderBrush = Brush.Parse("#CBD5E1"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#172033")
            }
        };
    }

    private Control ReportSummaryRow(string label, decimal budget, decimal actual, decimal variance, IBrush? background = null)
    {
        return new Border
        {
            Background = background ?? Brush.Parse("#EEF4FB"),
            BorderBrush = Brush.Parse("#CBD5E1"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("120,*,150,150,150"),
                Children = { Cell("", 0), Cell(label, 1, FontWeight.SemiBold), AmountCell(budget, 2, true), AmountCell(actual, 3, true), AmountCell(variance, 4, true) }
            }
        };
    }

    private Control ReportCarryoverRow(string label, decimal amount, IBrush? background = null)
    {
        return new Border
        {
            Background = background ?? Brush.Parse("#F8FAFC"),
            BorderBrush = Brush.Parse("#CBD5E1"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("120,*,150,150,150"),
                Children = { Cell("", 0), Cell(label, 1, FontWeight.SemiBold), Cell("", 2), AmountCell(amount, 3, true), Cell("", 4) }
            }
        };
    }

    private Control ReportNoteBlock()
    {
        var note = string.IsNullOrWhiteSpace(_reportNote.Text) ? " " : _reportNote.Text.Trim();
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#CBD5E1"),
            BorderThickness = new Thickness(1, 1, 1, 1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "注記", FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#172033") },
                    new TextBlock { Text = note, Foreground = Brush.Parse("#243044"), TextWrapping = TextWrapping.Wrap, MinHeight = 54 }
                }
            }
        };
    }

    private async Task<List<Option<long?>>> GetCategoryOptionsAsync(bool includeBlank)
    {
        var options = includeBlank ? [new Option<long?>(null, "未設定")] : new List<Option<long?>>();
        if (_database is null || _company is null) return options;
        options.AddRange((await _database.GetCategoriesAsync(_company.CompanyId)).Select(x => new Option<long?>(x.CategoryId, $"{(x.Kind == "income" ? "収入" : "支出")} / {x.Code} {x.Name}")));
        return options;
    }

    private (DateTime Start, DateTime End) GetPeriod()
    {
        var start = (_periodStart.SelectedDate?.DateTime ?? DateTime.Today).Date;
        var end = (_periodEnd.SelectedDate?.DateTime ?? start).Date;
        return start <= end ? (start, end) : (end, start);
    }

    private DateTime GetFiscalStart() => (_fiscalYearStart.SelectedDate?.DateTime ?? DateTime.Today).Date;
    private void SetMessage(string message, bool error) { _message.Text = message; _message.Foreground = error ? Brush.Parse("#B42318") : Brush.Parse("#1E6B52"); }
    private void SetError(string message) => SetMessage(message, true);

    private static int ParseInt(string? text, string label)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)) throw new InvalidOperationException($"{label}は整数で入力してください。");
        return value;
    }

    private static decimal ParseAmount(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) || value < 0) throw new InvalidOperationException($"{label}は0以上の数値で入力してください。");
        return value;
    }

    private static decimal ParseSignedAmount(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var normalized = text.Replace("△", "-", StringComparison.Ordinal).Trim();
        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out var value)) throw new InvalidOperationException($"{label}は数値で入力してください。");
        return value;
    }

    private static string SelectedValue(ComboBox combo, string fallback) => combo.SelectedItem is Option<string> option ? option.Value : fallback;
    private static T? SelectedValue<T>(ComboBox combo, T? fallback) => combo.SelectedItem is Option<T> option ? option.Value : fallback;
    private static string AccountTypeLabel(string type) => type switch { "asset" => "資産", "liability" => "負債", "equity" => "純資産", "revenue" => "収益", "expense" => "費用", _ => type };
    private static string StatusLabel(string status) => status switch { "reviewed" => "確認済み", "finalized" => "確定", _ => "仮" };

    private static string BuildReviewDescription(CashFlowReviewRow row)
    {
        var prefix = row.IsChanged ? "[変更あり] " : row.IsComposite ? "[内訳] " : "";
        return prefix + row.Description;
    }

    private static ComboBox Combo<T>(IReadOnlyList<Option<T>> options, Func<Option<T>, bool> predicate)
    {
        return new ComboBox { ItemsSource = options, SelectedItem = options.FirstOrDefault(predicate) ?? options[0], MinWidth = 120 };
    }

    private static Control Header(ColumnDefinitions columns, IReadOnlyList<string> labels)
    {
        var grid = new Grid { ColumnDefinitions = columns };
        for (var i = 0; i < labels.Count; i++) grid.Children.Add(HeaderCell(labels[i], i));
        return new Border { Background = Brush.Parse("#E6E9ED"), BorderBrush = Brush.Parse("#8A8F96"), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6), Child = grid };
    }

    private static TextBlock HeaderCell(string text, int column)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#172033") };
        Grid.SetColumn(block, column);
        return block;
    }

    private static Border RowBorder(Control child) => new() { Background = Brushes.White, BorderBrush = Brush.Parse("#D9DEE7"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 7), Child = child };

    private static TextBlock Cell(string text, int column, FontWeight weight = default, bool rightAlign = false)
    {
        var block = new TextBlock { Text = text, FontWeight = weight == default ? FontWeight.Normal : weight, Foreground = Brush.Parse("#243044"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(block, column);
        return block;
    }

    private static TextBlock AmountCell(decimal amount, int column, bool emphasized = false)
    {
        var block = new TextBlock { Text = FormatAmount(amount), FontWeight = emphasized ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Brush.Parse("#243044"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(block, column);
        return block;
    }

    private static string FormatAmount(decimal amount) => amount < 0 ? $"△{Math.Abs(amount):N0}" : amount.ToString("N0");

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static Control Field(string label, Control input, double minWidth) => new StackPanel { Spacing = 4, MinWidth = minWidth, Children = { ViewHelpers.Label(label), input } };
    private static Control ButtonField(Control button) => new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom, Children = { new Border { Height = 26 }, button } };
    private static Control WithMargin(Control control, Thickness margin) { control.Margin = margin; return control; }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
