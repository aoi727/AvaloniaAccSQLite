using System.Globalization;
using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class JournalEntryFormView : UserControl
{
    private const int VoucherRowCount = 10;
    private static readonly TaxInputOption[] GrossTaxInputOptions =
    [
        new("none", "税計算なし"),
        new("included", "税込")
    ];
    private static readonly TaxInputOption[] NetTaxInputOptions =
    [
        new("none", "税計算なし"),
        new("excluded", "税抜")
    ];
    private static readonly string[] HiddenTaxAccountsForGrossMethod = ["仮払消費税", "仮受消費税"];

    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly string? _editingEntryNumber;
    private readonly ComboBox _templateSelector = new() { MinWidth = 240 };
    private readonly TextBox _templateName = new() { PlaceholderText = "定型仕訳名" };
    private readonly DatePicker _entryDate = new() { SelectedDate = new DateTimeOffset(DateTime.Today) };
    private readonly TextBox _entryNumber = new() { PlaceholderText = "自動採番できます" };
    private readonly TextBox _reference = new() { PlaceholderText = "証憑番号、請求書番号など" };
    private readonly CheckBox _singleEntryMode = new() { Content = "単仕訳モード", IsChecked = true };
    private readonly TextBlock _debitTotal = ViewHelpers.Body("0");
    private readonly TextBlock _creditTotal = ViewHelpers.Body("0");
    private readonly Border _debitTotalBox = TotalBox();
    private readonly Border _creditTotalBox = TotalBox();
    private readonly TextBlock _balanceMessage = ViewHelpers.Body("");
    private readonly TextBlock _message = ViewHelpers.Body("振替伝票を入力してください。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("登録");
    private readonly Button _applyTemplateButton = ViewHelpers.SecondaryButton("読込");
    private readonly Button _saveTemplateButton = ViewHelpers.SecondaryButton("保存");
    private readonly Button _deleteTemplateButton = ViewHelpers.SecondaryButton("削除");
    private readonly Button _deleteButton = CreateDeleteButton();
    private readonly List<Account> _accounts = [];
    private readonly List<Account> _selectableAccounts = [];
    private readonly List<SubAccount> _subAccounts = [];
    private readonly List<TaxCode> _taxCodes = [];
    private readonly List<BusinessPartner> _partners = [];
    private readonly List<JournalTemplateSummary> _templates = [];
    private readonly List<VoucherRowControls> _rows = [];
    private Grid? _voucherGrid;
    private bool _isTaxExempt;
    private DateTime? _minimumEntryDate;
    private IReadOnlyList<TaxInputOption> _taxInputOptions = GrossTaxInputOptions;
    private string _taxEntryMethod = "gross";
    private bool _isUpdatingSingleModeRows;

    public JournalEntryFormView(SqliteDatabase database, AppUser user, Action backToDashboard, string? entryNumber = null)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _editingEntryNumber = entryNumber;
        Content = Build();
        _entryDate.SelectedDateChanged += async (_, _) => await EntryDateChangedAsync();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _applyTemplateButton.Click += async (_, _) => await ApplySelectedTemplateAsync();
        _saveTemplateButton.Click += async (_, _) => await SaveTemplateAsync();
        _deleteTemplateButton.Click += async (_, _) => await DeleteSelectedTemplateAsync();
        _templateSelector.SelectionChanged += (_, _) => SyncTemplateNameFromSelection();
        _singleEntryMode.IsCheckedChanged += (_, _) => ApplySingleEntryModeUi();
        _ = LoadAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("閉じる");
        backButton.Width = 120;
        backButton.Click += (_, _) => _backToDashboard();

        _saveButton.Width = 120;
        _applyTemplateButton.Width = 90;
        _saveTemplateButton.Width = 90;
        _deleteTemplateButton.Width = 90;
        _deleteButton.Width = 120;
        _deleteButton.IsVisible = _editingEntryNumber is not null;
        _deleteButton.Click += async (_, _) => await ConfirmAndDeleteAsync();
        backButton.Width = 120;
        _entryDate.MinWidth = 170;
        _entryNumber.MinWidth = 185;
        _reference.MinWidth = 170;
        _debitTotalBox.Child = _debitTotal;
        _creditTotalBox.Child = _creditTotal;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*"),
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        ViewHelpers.Heading(_editingEntryNumber is null ? "振替伝票" : $"振替伝票 {_editingEntryNumber}"),
                        ViewHelpers.Body(_user.CompanyName)
                    }
                }
            }
        };

        var totals = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,120,16,Auto,120"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                LabelText("借方合計", 0, HorizontalAlignment.Right),
                _debitTotalBox,
                LabelText("貸方合計", 3, HorizontalAlignment.Right),
                _creditTotalBox
            }
        };
        Grid.SetColumn(_debitTotalBox, 1);
        Grid.SetColumn(_creditTotalBox, 4);

        var status = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                totals,
                _balanceMessage,
                _message
            }
        };

        var meta = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,14,185,14,170,16,*,16,120,12,120,12,120"),
            Children =
            {
                Field("取引日", _entryDate, 0),
                Field("伝票番号", _entryNumber, 2),
                Field("証憑番号", _reference, 4),
                status,
                _saveButton,
                _deleteButton,
                backButton
            }
        };
        Grid.SetColumn(status, 6);
        Grid.SetColumn(_saveButton, 8);
        Grid.SetColumn(_deleteButton, 10);
        Grid.SetColumn(backButton, 12);

        var singleModePanel = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _singleEntryMode,
                ViewHelpers.Body("単仕訳モードでは各行を別伝票として保存し、借方金額を入力すると貸方金額へ同額を自動反映します。")
            }
        });

        var templatePanel = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                ViewHelpers.Heading("定型仕訳", 18),
                ViewHelpers.Body("よく使う入力内容を保存して、次回は読込から再利用できます。"),
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("240,12,180,12,90,12,90,12,90"),
                    Children =
                    {
                        Field("登録済み", _templateSelector, 0),
                        Field("保存名", _templateName, 2),
                        TemplateButtonField(_applyTemplateButton, 4),
                        TemplateButtonField(_saveTemplateButton, 6),
                        TemplateButtonField(_deleteTemplateButton, 8)
                    }
                }
            }
        });

        var table = BuildVoucherTable();

        var topArea = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 12,
            Children =
            {
                header,
                ViewHelpers.Panel(new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        meta,
                        singleModePanel,
                        templatePanel
                    }
                })
            }
        };

        var tableArea = new ScrollViewer
        {
            Margin = new Thickness(28, 0, 28, 28),
            Content = ViewHelpers.Panel(table)
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                topArea,
                tableArea
            }
        };
        Grid.SetRow(tableArea, 1);

        return layout;
    }

    private Control Field(string label, Control input, int column)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            Children = { ViewHelpers.Label(label), input }
        };
        Grid.SetColumn(panel, column);
        return panel;
    }

    private static Control TemplateButtonField(Control button, int column)
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
        return panel;
    }

    private Control BuildVoucherTable()
    {
        _voucherGrid = new Grid
        {
            RowDefinitions = BuildRowsDefinition(),
            ColumnDefinitions = new ColumnDefinitions("40,90,230,90,230,128,76,120,128,76,120,360")
        };
        var grid = _voucherGrid;

        AddHeader(grid, "No", 0);
        AddHeader(grid, "借方Code", 1);
        AddHeader(grid, "借方科目", 2);
        AddHeader(grid, "貸方Code", 3);
        AddHeader(grid, "貸方科目", 4);
        AddHeader(grid, "借方税区分", 5);
        AddHeader(grid, "入力", 6);
        AddHeader(grid, "借方金額", 7);
        AddHeader(grid, "貸方税区分", 8);
        AddHeader(grid, "入力", 9);
        AddHeader(grid, "貸方金額", 10);
        AddHeader(grid, "摘要", 11);

        for (var i = 0; i < VoucherRowCount; i++)
        {
            var row = new VoucherRowControls(i);
            _rows.Add(row);
            AddRowControls(_voucherGrid, row, (i * 2) + 1);
        }

        return new Border
        {
            Background = Brush.Parse("#D0D3D6"),
            BorderBrush = Brush.Parse("#8A8F96"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _voucherGrid
            }
        };
    }

    private static RowDefinitions BuildRowsDefinition()
    {
        var rows = new RowDefinitions("Auto");
        for (var i = 0; i < VoucherRowCount; i++)
        {
            rows.Add(new RowDefinition(GridLength.Auto));
            rows.Add(new RowDefinition(GridLength.Auto));
        }

        return rows;
    }

    private static void AddHeader(Grid grid, string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 4),
            Foreground = Brush.Parse("#172033")
        };
        Grid.SetRow(block, 0);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private void AddRowControls(Grid grid, VoucherRowControls row, int gridRow)
    {
        AddCell(grid, RowNumberText(row.RowNumber), gridRow, 0, 2);
        AddCell(grid, row.DebitCode, gridRow, 1);
        AddCell(grid, row.DebitAccount, gridRow, 2);
        AddCell(grid, row.CreditCode, gridRow, 3);
        AddCell(grid, row.CreditAccount, gridRow, 4);
        AddCell(grid, row.DebitTaxCode, gridRow, 5);
        AddCell(grid, row.DebitTaxInputType, gridRow, 6);
        AddCell(grid, row.DebitAmount, gridRow, 7);
        AddCell(grid, row.CreditTaxCode, gridRow, 8);
        AddCell(grid, row.CreditTaxInputType, gridRow, 9);
        AddCell(grid, row.CreditAmount, gridRow, 10);
        AddCell(grid, row.Description, gridRow, 11);

        AddCell(grid, SideText("補助"), gridRow + 1, 1);
        AddCell(grid, row.DebitSubAccount, gridRow + 1, 2);
        AddCell(grid, SideText("補助"), gridRow + 1, 3);
        AddCell(grid, row.CreditSubAccount, gridRow + 1, 4);
        AddCell(grid, PartnerInvoicePanel(row), gridRow + 1, 11);

        row.DebitCode.LostFocus += async (_, _) => await SelectAccountByCodeAsync(row.DebitCode, row.DebitAccount);
        row.CreditCode.LostFocus += async (_, _) => await SelectAccountByCodeAsync(row.CreditCode, row.CreditAccount);
        row.DebitAccount.SelectionChanged += async (_, _) =>
        {
            await AccountSelectionChangedAsync(row.DebitCode, row.DebitAccount, row.DebitSubAccount, row.DebitTaxCode);
            SyncSingleModeRow(row);
        };
        row.CreditAccount.SelectionChanged += async (_, _) => await AccountSelectionChangedAsync(row.CreditCode, row.CreditAccount, row.CreditSubAccount, row.CreditTaxCode);
        row.DebitSubAccount.SelectionChanged += (_, _) => SyncSingleModeRow(row);
        row.DebitTaxCode.SelectionChanged += (_, _) => UpdateInvoiceInfo(row);
        row.CreditTaxCode.SelectionChanged += (_, _) => UpdateInvoiceInfo(row);
        row.DebitTaxInputType.SelectionChanged += (_, _) => UpdateInvoiceInfo(row);
        row.CreditTaxInputType.SelectionChanged += (_, _) => UpdateInvoiceInfo(row);
        row.Partner.SelectionChanged += (_, _) => UpdateInvoiceInfo(row);
        row.ClearPartnerButton.Click += (_, _) =>
        {
            row.Partner.SelectedIndex = -1;
            UpdateInvoiceInfo(row);
        };
        row.DebitAmount.TextChanged += (_, _) =>
        {
            SyncSingleModeRow(row);
            UpdateTotals();
            UpdateInvoiceInfo(row);
        };
        row.CreditAmount.TextChanged += (_, _) =>
        {
            UpdateTotals();
            UpdateInvoiceInfo(row);
        };
    }

    private static Control PartnerInvoicePanel(VoucherRowControls row)
    {
        row.Partner.MinHeight = 28;
        row.ClearPartnerButton.MinHeight = 28;
        row.ClearPartnerButton.Width = 64;
        row.InvoiceNumber.MinHeight = 28;
        row.InvoiceInfo.Margin = new Thickness(0, 3, 0, 0);

        var partnerLabel = SideText("取引先");
        var invoiceLabel = SideText("請求書");

        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,70,8,Auto,120"),
            Children =
            {
                partnerLabel,
                row.Partner,
                row.ClearPartnerButton,
                invoiceLabel,
                row.InvoiceNumber,
                row.InvoiceInfo
            }
        };
        Grid.SetColumn(row.Partner, 1);
        Grid.SetColumn(row.ClearPartnerButton, 2);
        Grid.SetColumn(invoiceLabel, 4);
        Grid.SetColumn(row.InvoiceNumber, 5);
        Grid.SetRow(row.InvoiceInfo, 1);
        Grid.SetColumn(row.InvoiceInfo, 1);
        Grid.SetColumnSpan(row.InvoiceInfo, 5);
        return panel;
    }

    private static TextBlock RowNumberText(int rowNumber)
    {
        return new TextBlock
        {
            Text = rowNumber.ToString(),
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#243044")
        };
    }

    private static TextBlock SideText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#243044")
        };
    }

    private static void AddCell(Grid grid, Control control, int row, int column, int rowSpan = 1)
    {
        control.Margin = new Thickness(3, 2);
        if (control is TextBox textBox)
        {
            textBox.MinHeight = 28;
            if (rowSpan > 1)
            {
                textBox.MinHeight = 60;
                textBox.VerticalContentAlignment = VerticalAlignment.Top;
            }
        }
        if (control is ComboBox comboBox)
        {
            comboBox.MinHeight = 28;
            comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        if (rowSpan > 1)
        {
            Grid.SetRowSpan(control, rowSpan);
        }
        grid.Children.Add(control);
    }

    private static TextBlock LabelText(string text, int column, HorizontalAlignment alignment)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private static Border TotalBox()
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#8A8F96"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            MinHeight = 30
        };
    }

    private static Button CreateDeleteButton()
    {
        var button = ViewHelpers.SecondaryButton("削除");
        button.Background = Brush.Parse("#B42318");
        button.Foreground = Brushes.White;
        return button;
    }

    private async Task LoadAsync()
    {
        try
        {
            var companySettings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            _isTaxExempt = companySettings.IsTaxExempt;
            _minimumEntryDate = new DateTime(companySettings.FiscalYearStart.Year, companySettings.FiscalYearStart.Month, 1);
            _entryDate.MinYear = new DateTimeOffset(_minimumEntryDate.Value);
            _taxEntryMethod = !_isTaxExempt && companySettings.TaxEntryMethod is "net" ? "net" : "gross";
            _taxInputOptions = _taxEntryMethod == "net" ? NetTaxInputOptions : GrossTaxInputOptions;

            if (_editingEntryNumber is null)
            {
                EnsureEntryDateWithinFiscalYear();
            }

            _accounts.Clear();
            _accounts.AddRange(await _database.GetAccountsAsync(_user.CompanyId));
            _selectableAccounts.Clear();
            _selectableAccounts.AddRange(GetSelectableAccounts(_accounts, _taxEntryMethod));
            _taxCodes.Clear();
            _taxCodes.AddRange(await _database.GetTaxCodesAsync(_user.CompanyId));
            if (_taxCodes.Count == 0)
            {
                await _database.EnsureDefaultTaxCodesAsync(_user.CompanyId);
                _taxCodes.AddRange(await _database.GetTaxCodesAsync(_user.CompanyId));
            }

            _partners.Clear();
            _partners.AddRange((await _database.GetBusinessPartnersAsync(_user.CompanyId))
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code));

            foreach (var row in _rows)
            {
                row.SetSources(_selectableAccounts, _taxCodes, _partners, _taxInputOptions, GetDefaultTaxInputTypeIndex());
            }

            await LoadTemplatesAsync();

            ApplyTaxModeUi();

            if (_selectableAccounts.Count == 0)
            {
                SetMessage("勘定科目が登録されていません。先に勘定科目マスタを登録してください。", true);
                return;
            }

            try
            {
                _subAccounts.Clear();
                _subAccounts.AddRange(await _database.GetSubAccountsAsync(_user.CompanyId));
            }
            catch (Exception ex)
            {
                _subAccounts.Clear();
                SetMessage($"勘定科目は読み込みましたが、補助科目の読み込みに失敗しました: {ex.Message}", true);
            }

            if (_editingEntryNumber is null)
            {
                await SetNextEntryNumberIfNewAsync();
                ApplySingleEntryModeUi();
            }
            else
            {
                await LoadExistingVoucherAsync(_editingEntryNumber);
                _singleEntryMode.IsChecked = false;
                _singleEntryMode.IsEnabled = false;
                ApplySingleEntryModeUi();
            }

            UpdateTotals();
            RefreshInvoiceInfo();
            var methodLabel = _taxEntryMethod == "net" ? "税額分離方式" : "総額方式";
            SetMessage($"勘定科目 {_selectableAccounts.Count:N0} 件を読み込みました。現在は {methodLabel} の候補を表示しています。", false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
    }

    private async Task SetNextEntryNumberIfNewAsync()
    {
        if (_editingEntryNumber is not null)
        {
            return;
        }

        var date = _entryDate.SelectedDate?.DateTime.Date ?? DateTime.Today;
        _entryNumber.Text = await _database.GetNextEntryNumberAsync(_user.CompanyId, date);
    }

    private async Task LoadTemplatesAsync()
    {
        var selectedTemplateId = (_templateSelector.SelectedItem as JournalTemplateSummary)?.TemplateId;
        _templates.Clear();
        _templates.AddRange(await _database.GetJournalTemplateSummariesAsync(_user.CompanyId));
        _templateSelector.ItemsSource = null;
        _templateSelector.ItemsSource = _templates;
        _templateSelector.SelectedItem = selectedTemplateId.HasValue
            ? _templates.FirstOrDefault(x => x.TemplateId == selectedTemplateId.Value)
            : null;
    }

    private void SyncTemplateNameFromSelection()
    {
        if (_templateSelector.SelectedItem is JournalTemplateSummary template)
        {
            _templateName.Text = template.Name;
        }
    }

    private async Task ApplySelectedTemplateAsync()
    {
        if (_templateSelector.SelectedItem is not JournalTemplateSummary templateSummary)
        {
            SetMessage("読み込む定型仕訳を選択してください。", true);
            return;
        }

        var template = await _database.GetJournalTemplateAsync(_user.CompanyId, templateSummary.TemplateId);
        if (template is null)
        {
            SetMessage("選択した定型仕訳が見つかりません。", true);
            await LoadTemplatesAsync();
            return;
        }

        ClearVoucherRows();
        _reference.Text = template.Reference ?? "";
        _templateName.Text = template.Name;

        if (_editingEntryNumber is null)
        {
            _singleEntryMode.IsChecked = template.IsSingleEntryMode;
        }

        foreach (var templateRow in template.Rows.Where(x => x.RowNo >= 1 && x.RowNo <= _rows.Count))
        {
            ApplyTemplateRow(_rows[templateRow.RowNo - 1], templateRow);
        }

        ApplySingleEntryModeUi();
        UpdateTotals();
        RefreshInvoiceInfo();
        SetMessage($"定型仕訳を読み込みました: {template.Name}", false);
    }

    private async Task SaveTemplateAsync()
    {
        var templateName = _templateName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(templateName))
        {
            SetMessage("定型仕訳名を入力してください。", true);
            return;
        }

        var rows = BuildTemplateRowsFromForm();
        if (rows.Count == 0)
        {
            SetMessage("保存する明細がありません。", true);
            return;
        }

        await _database.SaveJournalTemplateAsync(
            _user.CompanyId,
            _user.UserId,
            templateName,
            _reference.Text,
            _singleEntryMode.IsChecked == true,
            rows);

        await LoadTemplatesAsync();
        _templateSelector.SelectedItem = _templates.FirstOrDefault(x => string.Equals(x.Name, templateName, StringComparison.Ordinal));
        SetMessage($"定型仕訳を保存しました: {templateName}", false);
    }

    private async Task DeleteSelectedTemplateAsync()
    {
        if (_templateSelector.SelectedItem is not JournalTemplateSummary template)
        {
            SetMessage("削除する定型仕訳を選択してください。", true);
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            SetMessage("定型仕訳の削除確認を表示できませんでした。", true);
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
            Title = "定型仕訳削除確認",
            Width = 460,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = ViewHelpers.Panel(new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    ViewHelpers.Heading("この定型仕訳を削除しますか", 20),
                    ViewHelpers.Body(template.Name),
                    buttons
                }
            })
        };

        executeButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        if (!await dialog.ShowDialog<bool>(owner))
        {
            return;
        }

        await _database.DeleteJournalTemplateAsync(_user.CompanyId, _user.UserId, template.TemplateId);
        _templateSelector.SelectedItem = null;
        _templateName.Text = "";
        await LoadTemplatesAsync();
        SetMessage($"定型仕訳を削除しました: {template.Name}", false);
    }

    private List<JournalTemplateRowData> BuildTemplateRowsFromForm()
    {
        var rows = new List<JournalTemplateRowData>();
        foreach (var row in _rows)
        {
            var data = new JournalTemplateRowData(
                row.RowNumber,
                NormalizeTemplateText(row.Description.Text),
                (row.Partner.SelectedItem as BusinessPartner)?.PartnerId,
                NormalizeTemplateText(row.InvoiceNumber.Text),
                (row.DebitAccount.SelectedItem as Account)?.AccountId,
                (row.DebitSubAccount.SelectedItem as SubAccount)?.SubAccountId,
                _isTaxExempt ? null : (row.DebitTaxCode.SelectedItem as TaxCode)?.TaxCodeId,
                _isTaxExempt ? "none" : GetTaxInputTypeValue(row.DebitTaxInputType.SelectedIndex, _taxInputOptions),
                ReadTemplateAmount(row.DebitAmount.Text),
                (row.CreditAccount.SelectedItem as Account)?.AccountId,
                (row.CreditSubAccount.SelectedItem as SubAccount)?.SubAccountId,
                _isTaxExempt ? null : (row.CreditTaxCode.SelectedItem as TaxCode)?.TaxCodeId,
                _isTaxExempt ? "none" : GetTaxInputTypeValue(row.CreditTaxInputType.SelectedIndex, _taxInputOptions),
                ReadTemplateAmount(row.CreditAmount.Text));

            if (IsTemplateRowEmpty(data))
            {
                continue;
            }

            rows.Add(data);
        }

        return rows;
    }

    private void ApplyTemplateRow(VoucherRowControls row, JournalTemplateRowData templateRow)
    {
        row.Description.Text = templateRow.Description ?? "";
        row.Partner.SelectedItem = FindPartner(templateRow.PartnerId);
        row.InvoiceNumber.Text = templateRow.InvoiceNumber ?? "";

        ApplyTemplateSide(
            row.DebitCode,
            row.DebitAccount,
            row.DebitSubAccount,
            row.DebitTaxCode,
            row.DebitTaxInputType,
            row.DebitAmount,
            templateRow.DebitAccountId,
            templateRow.DebitSubAccountId,
            templateRow.DebitTaxCodeId,
            templateRow.DebitTaxInputType,
            templateRow.DebitAmount);

        ApplyTemplateSide(
            row.CreditCode,
            row.CreditAccount,
            row.CreditSubAccount,
            row.CreditTaxCode,
            row.CreditTaxInputType,
            row.CreditAmount,
            templateRow.CreditAccountId,
            templateRow.CreditSubAccountId,
            templateRow.CreditTaxCodeId,
            templateRow.CreditTaxInputType,
            templateRow.CreditAmount);

        UpdateInvoiceInfo(row);
    }

    private void ApplyTemplateSide(
        TextBox codeBox,
        ComboBox accountCombo,
        ComboBox subAccountCombo,
        ComboBox taxCodeCombo,
        ComboBox taxInputTypeCombo,
        TextBox amountBox,
        int? accountId,
        int? subAccountId,
        int? taxCodeId,
        string taxInputType,
        decimal? amount)
    {
        var account = FindAccount(accountId);
        accountCombo.SelectedItem = account;
        codeBox.Text = account?.Code ?? "";
        UpdateSubAccountChoices(subAccountCombo, account);
        subAccountCombo.SelectedItem = FindSubAccount(subAccountId);
        taxCodeCombo.SelectedItem = FindTaxCode(taxCodeId);
        taxInputTypeCombo.SelectedIndex = GetTaxInputTypeIndex(taxInputType, _taxInputOptions);
        amountBox.Text = amount.HasValue ? amount.Value.ToString("0.##") : "";
    }

    private Account? FindAccount(int? accountId)
    {
        return accountId.HasValue
            ? _accounts.FirstOrDefault(x => x.AccountId == accountId.Value)
            : null;
    }

    private TaxCode? FindTaxCode(int? taxCodeId)
    {
        return taxCodeId.HasValue
            ? _taxCodes.FirstOrDefault(x => x.TaxCodeId == taxCodeId.Value)
            : null;
    }

    private static decimal? ReadTemplateAmount(string? text)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) && value > 0
            ? value
            : null;
    }

    private static string? NormalizeTemplateText(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool IsTemplateRowEmpty(JournalTemplateRowData row)
    {
        return row.DebitAccountId is null
            && row.CreditAccountId is null
            && !row.DebitAmount.HasValue
            && !row.CreditAmount.HasValue
            && string.IsNullOrWhiteSpace(row.Description)
            && row.PartnerId is null
            && string.IsNullOrWhiteSpace(row.InvoiceNumber);
    }

    private async Task EntryDateChangedAsync()
    {
        EnsureEntryDateWithinFiscalYear();
        await SetNextEntryNumberIfNewAsync();
        SyncSingleModeRows();
        RefreshInvoiceInfo();
    }

    private void ApplySingleEntryModeUi()
    {
        var isSingleMode = _singleEntryMode.IsChecked == true;
        foreach (var row in _rows)
        {
            row.CreditAmount.IsEnabled = !isSingleMode;
            row.CreditAmount.Opacity = isSingleMode ? 0.7 : 1;
        }

        SyncSingleModeRows();
    }

    private void SyncSingleModeRows()
    {
        if (_isUpdatingSingleModeRows || _singleEntryMode.IsChecked != true)
        {
            return;
        }

        _isUpdatingSingleModeRows = true;
        try
        {
            foreach (var row in _rows)
            {
                SyncSingleModeRow(row);
            }
        }
        finally
        {
            _isUpdatingSingleModeRows = false;
        }

        UpdateTotals();
        RefreshInvoiceInfo();
    }

    private void SyncSingleModeRow(VoucherRowControls row)
    {
        if (_isUpdatingSingleModeRows || _singleEntryMode.IsChecked != true)
        {
            return;
        }

        var debitAmountText = row.DebitAmount.Text?.Trim();
        if (string.IsNullOrWhiteSpace(debitAmountText))
        {
            row.CreditAmount.Text = "";
            return;
        }

        row.CreditAmount.Text = debitAmountText;
    }

    private bool IsSingleRowEmpty(VoucherRowControls row)
    {
        return row.DebitAccount.SelectedItem is null
            && row.CreditAccount.SelectedItem is null
            && string.IsNullOrWhiteSpace(row.DebitAmount.Text)
            && string.IsNullOrWhiteSpace(row.CreditAmount.Text)
            && string.IsNullOrWhiteSpace(row.Description.Text)
            && row.Partner.SelectedItem is null
            && string.IsNullOrWhiteSpace(row.InvoiceNumber.Text);
    }

    private static string IncrementEntryNumber(string entryNumber)
    {
        if (string.IsNullOrWhiteSpace(entryNumber) || entryNumber.Length < 4 || !int.TryParse(entryNumber[^4..], out var sequence))
        {
            throw new InvalidOperationException("伝票番号の下4桁は数字で入力してください。");
        }

        return entryNumber[..^4] + (sequence + 1).ToString("0000");
    }

    private void EnsureEntryDateWithinFiscalYear()
    {
        if (_minimumEntryDate is null)
        {
            return;
        }

        var selectedDate = _entryDate.SelectedDate?.DateTime.Date;
        if (selectedDate.HasValue && selectedDate.Value < _minimumEntryDate.Value)
        {
            _entryDate.SelectedDate = new DateTimeOffset(_minimumEntryDate.Value);
        }
    }

    private async Task LoadExistingVoucherAsync(string entryNumber)
    {
        var summaries = await _database.GetJournalVoucherSummariesAsync(_user.CompanyId);
        var summary = summaries.FirstOrDefault(x => x.EntryNumber == entryNumber);
        if (summary is not null)
        {
            _entryDate.SelectedDate = new DateTimeOffset(summary.EntryDate);
            _entryNumber.Text = summary.EntryNumber;
            _reference.Text = summary.Reference ?? "";
        }

        var lines = await _database.GetJournalLinesAsync(_user.CompanyId, entryNumber);
        var debitLines = lines.Where(x => x.Side == "debit").ToList();
        var creditLines = lines.Where(x => x.Side == "credit").ToList();
        var max = Math.Min(_rows.Count, Math.Max(debitLines.Count, creditLines.Count));

        for (var i = 0; i < max; i++)
        {
            if (i < debitLines.Count)
            {
                FillSide(_rows[i], debitLines[i], true);
            }

            if (i < creditLines.Count)
            {
                FillSide(_rows[i], creditLines[i], false);
            }

            _rows[i].Description.Text = debitLines.ElementAtOrDefault(i)?.Description
                ?? creditLines.ElementAtOrDefault(i)?.Description
                ?? "";

            var invoiceLine = debitLines.ElementAtOrDefault(i) ?? creditLines.ElementAtOrDefault(i);
            if (invoiceLine is not null)
            {
                _rows[i].Partner.SelectedItem = FindPartner(invoiceLine.PartnerId);
                _rows[i].InvoiceNumber.Text = invoiceLine.InvoiceNumber ?? "";
                UpdateInvoiceInfo(_rows[i]);
            }
        }
    }

    private void FillSide(VoucherRowControls row, JournalLine line, bool isDebit)
    {
        var account = _accounts.FirstOrDefault(x => x.AccountId == line.AccountId);
        var taxCode = line.TaxCodeId.HasValue
            ? _taxCodes.FirstOrDefault(x => x.TaxCodeId == line.TaxCodeId.Value)
            : null;
        var subAccount = FindSubAccount(line.SubAccountId);
        var inputTypeIndex = GetTaxInputTypeIndex(line.TaxInputType, _taxInputOptions);

        if (isDebit)
        {
            row.DebitCode.Text = account?.Code ?? line.AccountCode;
            row.DebitAccount.SelectedItem = account;
            UpdateSubAccountChoices(row.DebitSubAccount, account);
            if (subAccount is not null)
            {
                row.DebitSubAccount.SelectedItem = subAccount;
            }
            row.DebitTaxCode.SelectedItem = taxCode;
            row.DebitTaxInputType.SelectedIndex = inputTypeIndex;
            row.DebitAmount.Text = line.Amount.ToString("0.##");
        }
        else
        {
            row.CreditCode.Text = account?.Code ?? line.AccountCode;
            row.CreditAccount.SelectedItem = account;
            UpdateSubAccountChoices(row.CreditSubAccount, account);
            if (subAccount is not null)
            {
                row.CreditSubAccount.SelectedItem = subAccount;
            }
            row.CreditTaxCode.SelectedItem = taxCode;
            row.CreditTaxInputType.SelectedIndex = inputTypeIndex;
            row.CreditAmount.Text = line.Amount.ToString("0.##");
        }
    }

    private SubAccount? FindSubAccount(int? subAccountId)
    {
        return subAccountId.HasValue && subAccountId.Value > 0
            ? _subAccounts.FirstOrDefault(x => x.SubAccountId == subAccountId.Value)
            : null;
    }

    private BusinessPartner? FindPartner(int? partnerId)
    {
        return partnerId.HasValue
            ? _partners.FirstOrDefault(x => x.PartnerId == partnerId.Value)
            : null;
    }

    private async Task SelectAccountByCodeAsync(TextBox codeBox, ComboBox accountCombo)
    {
        var code = codeBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            accountCombo.SelectedIndex = -1;
            ClearCodeWarning(codeBox);
            return;
        }

        var account = _accounts.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            accountCombo.SelectedIndex = -1;
            SetCodeWarning(codeBox);
            await ShowWarningAsync($"科目コード {code} は登録されていません。勘定科目マスタに登録済みのコードを入力してください。");
            return;
        }

        if (!_selectableAccounts.Any(x => x.AccountId == account.AccountId))
        {
            accountCombo.SelectedIndex = -1;
            SetCodeWarning(codeBox);
            await ShowWarningAsync($"勘定コード {code} は現在の税処理方式では選択できません。");
            return;
        }

        ClearCodeWarning(codeBox);
        accountCombo.SelectedItem = account;
    }

    private static void SetCodeWarning(TextBox codeBox)
    {
        codeBox.BorderBrush = Brush.Parse("#D92D20");
        codeBox.BorderThickness = new Thickness(2);
    }

    private static void ClearCodeWarning(TextBox codeBox)
    {
        codeBox.BorderBrush = null;
        codeBox.BorderThickness = new Thickness(1);
    }

    private async Task ShowWarningAsync(string message)
    {
        SetMessage(message, true);

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var closeButton = ViewHelpers.PrimaryButton("OK");
        closeButton.Width = 100;

        var dialog = new Window
        {
            Title = "入力エラー",
            Width = 420,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(24),
                Background = Brushes.White,
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        ViewHelpers.Heading("科目コードを確認してください", 20),
                        ViewHelpers.Body(message),
                        closeButton
                    }
                }
            }
        };

        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    private async Task ShowNoticeAsync(string title, string message)
    {
        SetMessage(message, false);

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var closeButton = ViewHelpers.PrimaryButton("OK");
        closeButton.Width = 100;

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(24),
                Background = Brushes.White,
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        ViewHelpers.Heading(title, 20),
                        ViewHelpers.Body(message),
                        closeButton
                    }
                }
            }
        };

        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    private async Task ConfirmAndDeleteAsync()
    {
        if (string.IsNullOrWhiteSpace(_editingEntryNumber))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            SetMessage("削除確認ダイアログを表示できませんでした。", true);
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
                    ViewHelpers.Heading("この仕訳を削除しますか。", 22),
                    ViewHelpers.Body($"伝票番号: {_editingEntryNumber}"),
                    ViewHelpers.Body("削除するとその伝票に含まれるすべての仕訳行が削除され、残高も再計算されます。"),
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
            _deleteButton.IsEnabled = false;
            _saveButton.IsEnabled = false;
            await _database.DeleteJournalVoucherAsync(_user.CompanyId, _user.UserId, _editingEntryNumber);
            SetMessage($"仕訳を削除しました: {_editingEntryNumber}", false);
            _backToDashboard();
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            _deleteButton.IsEnabled = true;
            _saveButton.IsEnabled = true;
        }
    }

    private async Task ClearForNextInputAsync(DateTime entryDate)
    {
        _reference.Text = "";
        _entryNumber.Text = await _database.GetNextEntryNumberAsync(_user.CompanyId, entryDate);
        ClearVoucherRows();
        SyncSingleModeRows();
        UpdateTotals();
    }

    private void ClearVoucherRows()
    {
        foreach (var row in _rows)
        {
            ClearCodeWarning(row.DebitCode);
            ClearCodeWarning(row.CreditCode);

            row.DebitCode.Text = "";
            row.DebitAccount.SelectedIndex = -1;
            row.DebitSubAccount.ItemsSource = Array.Empty<SubAccount>();
            row.DebitSubAccount.SelectedIndex = -1;
            row.DebitSubAccount.IsEnabled = false;
            row.DebitTaxCode.SelectedIndex = _taxCodes.Count > 0 ? 0 : -1;
            row.DebitTaxInputType.SelectedIndex = GetDefaultTaxInputTypeIndex();
            row.DebitAmount.Text = "";

            row.CreditCode.Text = "";
            row.CreditAccount.SelectedIndex = -1;
            row.CreditSubAccount.ItemsSource = Array.Empty<SubAccount>();
            row.CreditSubAccount.SelectedIndex = -1;
            row.CreditSubAccount.IsEnabled = false;
            row.CreditTaxCode.SelectedIndex = _taxCodes.Count > 0 ? 0 : -1;
            row.CreditTaxInputType.SelectedIndex = GetDefaultTaxInputTypeIndex();
            row.CreditAmount.Text = "";

            row.Description.Text = "";
            row.Partner.SelectedIndex = -1;
            row.InvoiceNumber.Text = "";
            UpdateInvoiceInfo(row);
        }
    }

    private async Task AccountSelectionChangedAsync(TextBox codeBox, ComboBox accountCombo, ComboBox subAccountCombo, ComboBox taxCodeCombo)
    {
        await Task.CompletedTask;
        if (accountCombo.SelectedItem is Account account)
        {
            codeBox.Text = account.Code;
            UpdateSubAccountChoices(subAccountCombo, account);
            ApplyDefaultTaxCode(taxCodeCombo, account);
        }
        else
        {
            codeBox.Text = "";
            UpdateSubAccountChoices(subAccountCombo, null);
        }
    }

    private void ApplyDefaultTaxCode(ComboBox taxCodeCombo, Account account)
    {
        if (!account.DefaultTaxCodeId.HasValue)
        {
            return;
        }

        var taxCode = _taxCodes.FirstOrDefault(x => x.TaxCodeId == account.DefaultTaxCodeId.Value);
        if (taxCode is not null)
        {
            taxCodeCombo.SelectedItem = taxCode;
        }
    }

    private void UpdateSubAccountChoices(ComboBox subAccountCombo, Account? account)
    {
        if (account is null)
        {
            subAccountCombo.ItemsSource = Array.Empty<SubAccount>();
            subAccountCombo.SelectedIndex = -1;
            subAccountCombo.IsEnabled = false;
            return;
        }

        var choices = _subAccounts
            .Where(x => x.AccountId == account.AccountId && x.IsActive)
            .OrderBy(x => x.Code)
            .ToList();

        var selected = subAccountCombo.SelectedItem as SubAccount;
        subAccountCombo.ItemsSource = choices;
        subAccountCombo.IsEnabled = choices.Count > 0;
        if (selected is not null && choices.Any(x => x.SubAccountId == selected.SubAccountId))
        {
            subAccountCombo.SelectedItem = choices.First(x => x.SubAccountId == selected.SubAccountId);
            return;
        }

        subAccountCombo.SelectedItem = choices.Count == 1 && choices[0].Code == "0"
            ? choices[0]
            : null;
    }

    private async Task SaveAsync()
    {
        try
        {
            _saveButton.IsEnabled = false;
            var date = _entryDate.SelectedDate?.DateTime.Date ?? DateTime.Today;
            if (_singleEntryMode.IsChecked == true)
            {
                await SaveSingleEntryModeAsync(date);
                return;
            }

            var inputs = BuildJournalInputs(date);
            var debitTotal = inputs.Where(x => x.Side == "debit").Sum(x => x.Amount);
            var creditTotal = inputs.Where(x => x.Side == "credit").Sum(x => x.Amount);
            if (inputs.Count == 0)
            {
                SetMessage("明細を入力してください。", true);
                return;
            }

            if (debitTotal != creditTotal)
            {
                SetMessage("借方合計と貸方合計が一致していません。", true);
                return;
            }

            inputs = await ConfirmMissingSubAccountsAsync(inputs);
            if (inputs.Count == 0)
            {
                return;
            }

            await _database.SaveJournalVoucherAsync(
                _user.CompanyId,
                _entryNumber.Text?.Trim() ?? "",
                date,
                _reference.Text,
                _user.UserId,
                inputs,
                _editingEntryNumber);

            await ShowNoticeAsync(
                "登録完了",
                _editingEntryNumber is null
                    ? "振替伝票を登録しました。次の入力に備えて画面をクリアします。"
                    : "振替伝票を更新しました。");
            if (_editingEntryNumber is not null)
            {
                _backToDashboard();
                return;
            }

            await ClearForNextInputAsync(date);
            SetMessage("振替伝票を登録しました。次の伝票を入力できます。", false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            _saveButton.IsEnabled = true;
        }
    }

    private async Task SaveSingleEntryModeAsync(DateTime date)
    {
        if (_editingEntryNumber is not null)
        {
            SetMessage("既存伝票の編集では単仕訳モードを使用できません。", true);
            return;
        }

        var voucherRows = await BuildSingleJournalVoucherRowsAsync(date);
        if (voucherRows.Count == 0)
        {
            SetMessage("借方行を入力してください。", true);
            return;
        }

        await _database.SaveJournalVouchersAsync(
            _user.CompanyId,
            _entryNumber.Text?.Trim() ?? "",
            date,
            _reference.Text,
            _user.UserId,
            voucherRows.Select(x => (IReadOnlyList<JournalLineInput>)x.Lines).ToList());

        var lastEntryNumber = voucherRows[^1].EntryNumber;
        await ShowNoticeAsync(
            "登録完了",
            voucherRows.Count == 1
                ? $"単仕訳を登録しました。伝票番号: {voucherRows[0].EntryNumber}"
                : $"単仕訳を {voucherRows.Count} 件登録しました。最終伝票番号: {lastEntryNumber}");

        await ClearForNextInputAsync(date);
        SetMessage($"単仕訳を {voucherRows.Count} 件登録しました。", false);
    }

    private List<JournalLineInput> BuildJournalInputs(DateTime entryDate)
    {
        var inputs = new List<JournalLineInput>();
        foreach (var row in _rows)
        {
            var description = row.Description.Text;
            AddSideInput(inputs, row, true, description, entryDate);
            AddSideInput(inputs, row, false, description, entryDate);
        }

        return inputs;
    }

    private async Task<List<SingleVoucherDraft>> BuildSingleJournalVoucherRowsAsync(DateTime entryDate)
    {
        var result = new List<SingleVoucherDraft>();
        var currentEntryNumber = _entryNumber.Text?.Trim() ?? "";
        foreach (var row in _rows)
        {
            if (IsSingleRowEmpty(row))
            {
                continue;
            }

            var description = row.Description.Text;
            var debitInput = BuildSideInput(
                row,
                true,
                description,
                entryDate,
                row.DebitAccount.SelectedItem as Account,
                row.DebitSubAccount.SelectedItem as SubAccount,
                row.DebitAmount.Text,
                _isTaxExempt ? null : row.DebitTaxCode.SelectedItem as TaxCode,
                _isTaxExempt ? "none" : GetTaxInputTypeValue(row.DebitTaxInputType.SelectedIndex, _taxInputOptions));

            var creditInput = BuildSideInput(
                row,
                false,
                description,
                entryDate,
                row.CreditAccount.SelectedItem as Account,
                row.CreditSubAccount.SelectedItem as SubAccount,
                row.CreditAmount.Text,
                _isTaxExempt ? null : row.CreditTaxCode.SelectedItem as TaxCode,
                _isTaxExempt ? "none" : GetTaxInputTypeValue(row.CreditTaxInputType.SelectedIndex, _taxInputOptions));

            var lines = await ConfirmMissingSubAccountsAsync([debitInput, creditInput]);
            if (lines.Count == 0)
            {
                return [];
            }

            result.Add(new SingleVoucherDraft(currentEntryNumber, lines));
            currentEntryNumber = IncrementEntryNumber(currentEntryNumber);
        }

        return result;
    }

    private void AddSideInput(List<JournalLineInput> inputs, VoucherRowControls row, bool isDebit, string? description, DateTime entryDate)
    {
        var account = (isDebit ? row.DebitAccount.SelectedItem : row.CreditAccount.SelectedItem) as Account;
        var amountBox = isDebit ? row.DebitAmount : row.CreditAmount;
        if (account is null && string.IsNullOrWhiteSpace(amountBox.Text))
        {
            return;
        }

        if (account is null)
        {
            throw new InvalidOperationException($"{row.RowNumber}行目の{(isDebit ? "借方" : "貸方")}科目を選択してください。");
        }

        if (!decimal.TryParse(amountBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            throw new InvalidOperationException($"{row.RowNumber}行目の{(isDebit ? "借方" : "貸方")}金額を入力してください。");
        }

        var taxCode = _isTaxExempt ? null : (isDebit ? row.DebitTaxCode.SelectedItem : row.CreditTaxCode.SelectedItem) as TaxCode;
        var subAccount = (isDebit ? row.DebitSubAccount.SelectedItem : row.CreditSubAccount.SelectedItem) as SubAccount;
        var inputTypeCombo = isDebit ? row.DebitTaxInputType : row.CreditTaxInputType;
        var taxInputType = _isTaxExempt ? "none" : GetTaxInputTypeValue(inputTypeCombo.SelectedIndex, _taxInputOptions);
        inputs.Add(BuildSideInput(
            row,
            isDebit,
            description,
            entryDate,
            account,
            subAccount,
            amountBox.Text,
            taxCode,
            taxInputType));
    }

    private JournalLineInput BuildSideInput(
        VoucherRowControls row,
        bool isDebit,
        string? description,
        DateTime entryDate,
        Account? account,
        SubAccount? subAccount,
        string? amountText,
        TaxCode? taxCode,
        string taxInputType)
    {
        if (account is null)
        {
            throw new InvalidOperationException($"{row.RowNumber}行の{(isDebit ? "借方" : "貸方")}科目を選択してください。");
        }

        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            throw new InvalidOperationException($"{row.RowNumber}行の{(isDebit ? "借方" : "貸方")}金額を入力してください。");
        }

        var partner = row.Partner.SelectedItem as BusinessPartner;
        var taxDetails = CalculateTaxDetails(amount, taxInputType, taxCode, partner, entryDate);

        return new JournalLineInput(
            isDebit ? "debit" : "credit",
            account.AccountId,
            subAccount?.SubAccountId,
            amount,
            taxCode?.TaxCodeId,
            taxCode?.TaxRate,
            taxDetails.TaxAmount,
            taxDetails.CreditableTaxAmount,
            taxDetails.NonCreditableTaxAmount,
            taxInputType,
            description,
            partner?.PartnerId,
            _isTaxExempt ? null : row.InvoiceNumber.Text,
            _isTaxExempt ? null : partner?.RegistrationNumber,
            _isTaxExempt ? null : taxDetails.InvoiceStatus,
            _isTaxExempt ? null : taxDetails.PurchaseCreditRate);
    }

    private async Task<List<JournalLineInput>> ConfirmMissingSubAccountsAsync(List<JournalLineInput> inputs)
    {
        if (inputs.All(x => x.SubAccountId.HasValue))
        {
            return inputs;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            throw new InvalidOperationException("補助科目未指定の確認ダイアログを表示できませんでした。");
        }

        var registerButton = ViewHelpers.PrimaryButton("0 で登録する");
        registerButton.Width = 130;
        var cancelButton = ViewHelpers.SecondaryButton("キャンセル");
        cancelButton.Width = 120;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { registerButton, cancelButton }
        };

        var dialog = new Window
        {
            Title = "補助科目未指定の確認",
            Width = 460,
            Height = 230,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(24),
                Background = Brushes.White,
                Child = new StackPanel
                {
                    Spacing = 16,
                    Children =
                    {
                        ViewHelpers.Heading("補助科目が未指定の行があります", 20),
                        ViewHelpers.Body("補助科目が指定されていない行は、sub_account_id に 0 を入れて登録します。続行しますか。"),
                        buttons
                    }
                }
            }
        };

        registerButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
        {
            SetMessage("補助科目未指定のため登録をキャンセルしました。", true);
            return [];
        }

        return inputs
            .Select(x => x.SubAccountId.HasValue ? x : x with { SubAccountId = 0 })
            .ToList();
    }

    private void UpdateTotals()
    {
        var debit = _rows.Sum(row => TryReadAmount(row.DebitAmount.Text));
        var credit = _rows.Sum(row => TryReadAmount(row.CreditAmount.Text));
        _debitTotal.Text = debit.ToString("N0");
        _creditTotal.Text = credit.ToString("N0");
        _balanceMessage.Text = $"差額 {(debit - credit):N0}";
        var isBalanced = debit == credit;
        _balanceMessage.Foreground = isBalanced ? Brush.Parse("#1E6B52") : Brush.Parse("#B42318");
        _debitTotalBox.Background = isBalanced ? Brushes.White : Brush.Parse("#FEE4E2");
        _creditTotalBox.Background = isBalanced ? Brushes.White : Brush.Parse("#FEE4E2");
        _debitTotalBox.BorderBrush = isBalanced ? Brush.Parse("#8A8F96") : Brush.Parse("#D92D20");
        _creditTotalBox.BorderBrush = isBalanced ? Brush.Parse("#8A8F96") : Brush.Parse("#D92D20");
    }

    private void RefreshInvoiceInfo()
    {
        foreach (var row in _rows)
        {
            UpdateInvoiceInfo(row);
        }
    }

    private void UpdateInvoiceInfo(VoucherRowControls row)
    {
        if (_isTaxExempt)
        {
            row.InvoiceInfo.Text = "";
            return;
        }

        var entryDate = _entryDate.SelectedDate?.DateTime.Date ?? DateTime.Today;
        var partner = row.Partner.SelectedItem as BusinessPartner;
        var target = GetInvoiceTarget(row);
        if (target.TaxCode is null || !target.TaxCode.IsPurchaseCredit)
        {
            row.InvoiceInfo.Text = partner is null
                ? "仕入税区分を選ぶとインボイス情報を確認できます。"
                : PartnerStatusText(partner);
            row.InvoiceInfo.Foreground = Brush.Parse("#4A5568");
            return;
        }

        if (GetTaxInputTypeValue(target.InputTypeIndex, _taxInputOptions) == "none")
        {
            row.InvoiceInfo.Text = partner is null
                ? "税計算なしです。この行では消費税計算を行いません。"
                : $"{PartnerStatusText(partner)} / 税計算なし";
            row.InvoiceInfo.Foreground = Brush.Parse("#4A5568");
            return;
        }

        if (partner is null)
        {
            row.InvoiceInfo.Text = target.TaxCode.RequiresInvoice
                ? "仕入税額控除には取引先の選択が必要です。"
                : "取引先未選択。控除対象外の税区分です。";
            row.InvoiceInfo.Foreground = Brush.Parse("#B42318");
            return;
        }

        var amount = TryReadAmount(target.AmountText);
        var inputType = GetTaxInputTypeValue(target.InputTypeIndex, _taxInputOptions);
        var details = CalculateTaxDetails(amount, inputType, target.TaxCode, partner, entryDate);
        var registrationNumber = string.IsNullOrWhiteSpace(partner.RegistrationNumber)
            ? "登録番号なし"
            : partner.RegistrationNumber;

        row.InvoiceInfo.Text = $"{ToInvoiceStatusLabel(partner.InvoiceStatus)} / {registrationNumber} / 控除率 {details.PurchaseCreditRate:0.##}% / 消費税 {details.TaxAmount:N0} / 控除可 {details.CreditableTaxAmount:N0} / 控除不可 {details.NonCreditableTaxAmount:N0}";
        row.InvoiceInfo.Foreground = details.PurchaseCreditRate == 100m
            ? Brush.Parse("#1E6B52")
            : Brush.Parse("#B54708");
    }

    private static InvoiceTarget GetInvoiceTarget(VoucherRowControls row)
    {
        if (row.DebitTaxCode.SelectedItem is TaxCode debitTaxCode && debitTaxCode.IsPurchaseCredit)
        {
            return new InvoiceTarget(debitTaxCode, row.DebitAmount.Text, row.DebitTaxInputType.SelectedIndex);
        }

        if (row.CreditTaxCode.SelectedItem is TaxCode creditTaxCode && creditTaxCode.IsPurchaseCredit)
        {
            return new InvoiceTarget(creditTaxCode, row.CreditAmount.Text, row.CreditTaxInputType.SelectedIndex);
        }

        return new InvoiceTarget(row.DebitTaxCode.SelectedItem as TaxCode, row.DebitAmount.Text, row.DebitTaxInputType.SelectedIndex);
    }

    private static string PartnerStatusText(BusinessPartner partner)
    {
        var registrationNumber = string.IsNullOrWhiteSpace(partner.RegistrationNumber)
            ? "登録番号なし"
            : partner.RegistrationNumber;
        return $"{ToInvoiceStatusLabel(partner.InvoiceStatus)} / {registrationNumber}";
    }

    private static string ToInvoiceStatusLabel(string invoiceStatus)
    {
        return invoiceStatus switch
        {
            "qualified" => "適格",
            "unregistered" => "登録なし",
            "exempt" => "免税",
            "unknown" => "未確認",
            _ => invoiceStatus
        };
    }

    private static decimal TryReadAmount(string? text)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : 0;
    }

    private static TaxCalculationResult CalculateTaxDetails(
        decimal amount,
        string taxInputType,
        TaxCode? taxCode,
        BusinessPartner? partner,
        DateTime entryDate)
    {
        if (taxCode is null || !taxCode.IsTaxable || taxCode.TaxRate <= 0)
        {
            return new TaxCalculationResult(0, 0, 0, null, null);
        }

        if (taxInputType == "none")
        {
            return new TaxCalculationResult(0, 0, 0, null, null);
        }

        var tax = taxInputType == "included"
            ? amount * taxCode.TaxRate / (100 + taxCode.TaxRate)
            : amount * taxCode.TaxRate / 100;
        var taxAmount = Math.Round(tax, 0, MidpointRounding.AwayFromZero);
        if (!taxCode.IsPurchaseCredit)
        {
            return new TaxCalculationResult(taxAmount, 0, 0, null, null);
        }

        var invoiceStatus = partner?.InvoiceStatus ?? "unknown";
        var purchaseCreditRate = taxCode.RequiresInvoice
            ? GetPurchaseCreditRate(invoiceStatus, entryDate)
            : taxCode.DefaultPurchaseCreditRate;

        purchaseCreditRate = Math.Clamp(purchaseCreditRate, 0m, 100m);
        var creditableTax = Math.Round(taxAmount * purchaseCreditRate / 100m, 0, MidpointRounding.AwayFromZero);
        return new TaxCalculationResult(
            taxAmount,
            creditableTax,
            taxAmount - creditableTax,
            invoiceStatus,
            purchaseCreditRate);
    }

    private static decimal GetPurchaseCreditRate(string invoiceStatus, DateTime entryDate)
    {
        if (invoiceStatus == "qualified")
        {
            return 100m;
        }

        if (invoiceStatus is not ("unregistered" or "exempt"))
        {
            return 0m;
        }

        if (entryDate < new DateTime(2026, 10, 1))
        {
            return 80m;
        }

        if (entryDate < new DateTime(2029, 10, 1))
        {
            return 50m;
        }

        return 0m;
    }

    private sealed record TaxCalculationResult(
        decimal TaxAmount,
        decimal CreditableTaxAmount,
        decimal NonCreditableTaxAmount,
        string? InvoiceStatus,
        decimal? PurchaseCreditRate);

    private sealed record SingleVoucherDraft(string EntryNumber, IReadOnlyList<JournalLineInput> Lines);

    private sealed record InvoiceTarget(TaxCode? TaxCode, string? AmountText, int InputTypeIndex);

    private static IReadOnlyList<Account> GetSelectableAccounts(IEnumerable<Account> accounts, string taxEntryMethod)
    {
        if (taxEntryMethod != "gross")
        {
            return accounts.ToList();
        }

        return accounts
            .Where(account => !HiddenTaxAccountsForGrossMethod.Contains(account.Name))
            .ToList();
    }

    private static string GetTaxInputTypeValue(int selectedIndex, IReadOnlyList<TaxInputOption> options)
    {
        return selectedIndex >= 0 && selectedIndex < options.Count
            ? options[selectedIndex].Value
            : "none";
    }

    private int GetDefaultTaxInputTypeIndex()
    {
        if (_isTaxExempt)
        {
            return 0;
        }

        var defaultValue = _taxEntryMethod == "gross" ? "included" : "excluded";
        return GetTaxInputTypeIndex(defaultValue, _taxInputOptions);
    }

    private static int GetTaxInputTypeIndex(string? taxInputType, IReadOnlyList<TaxInputOption> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Value, taxInputType, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private void ApplyTaxModeUi()
    {
        if (_voucherGrid is not null)
        {
            _voucherGrid.ColumnDefinitions[5].Width = _isTaxExempt ? new GridLength(0) : new GridLength(128);
            _voucherGrid.ColumnDefinitions[6].Width = _isTaxExempt ? new GridLength(0) : new GridLength(76);
            _voucherGrid.ColumnDefinitions[8].Width = _isTaxExempt ? new GridLength(0) : new GridLength(128);
            _voucherGrid.ColumnDefinitions[9].Width = _isTaxExempt ? new GridLength(0) : new GridLength(76);
        }

        foreach (var row in _rows)
        {
            row.DebitTaxCode.IsVisible = !_isTaxExempt;
            row.DebitTaxInputType.IsVisible = !_isTaxExempt;
            row.CreditTaxCode.IsVisible = !_isTaxExempt;
            row.CreditTaxInputType.IsVisible = !_isTaxExempt;
            row.InvoiceNumber.IsVisible = !_isTaxExempt;
            row.InvoiceInfo.IsVisible = !_isTaxExempt;

            row.DebitTaxCode.IsEnabled = !_isTaxExempt;
            row.DebitTaxInputType.IsEnabled = !_isTaxExempt;
            row.CreditTaxCode.IsEnabled = !_isTaxExempt;
            row.CreditTaxInputType.IsEnabled = !_isTaxExempt;

            if (_isTaxExempt)
            {
                row.DebitTaxCode.SelectedItem = null;
                row.CreditTaxCode.SelectedItem = null;
                row.DebitTaxInputType.SelectedIndex = GetDefaultTaxInputTypeIndex();
                row.CreditTaxInputType.SelectedIndex = GetDefaultTaxInputTypeIndex();
                row.InvoiceNumber.Text = "";
                row.InvoiceInfo.Text = "";
            }
        }
    }

    private void SetMessage(string text, bool isError)
    {
        _message.Text = text;
        _message.Foreground = isError ? Brush.Parse("#B42318") : Brush.Parse("#4A5568");
    }

    private sealed class VoucherRowControls
    {
        public VoucherRowControls(int index)
        {
            RowNumber = index + 1;
            DebitTaxInputType.SelectedIndex = 0;
            CreditTaxInputType.SelectedIndex = 0;
        }

        public int RowNumber { get; }
        public TextBox DebitCode { get; } = new();
        public ComboBox DebitAccount { get; } = new();
        public ComboBox DebitSubAccount { get; } = new() { IsEnabled = false };
        public ComboBox DebitTaxCode { get; } = new();
        public ComboBox DebitTaxInputType { get; } = new();
        public TextBox DebitAmount { get; } = new();
        public TextBox CreditCode { get; } = new();
        public ComboBox CreditAccount { get; } = new();
        public ComboBox CreditSubAccount { get; } = new() { IsEnabled = false };
        public ComboBox CreditTaxCode { get; } = new();
        public ComboBox CreditTaxInputType { get; } = new();
        public TextBox CreditAmount { get; } = new();
        public ComboBox Partner { get; } = new();
        public Button ClearPartnerButton { get; } = ViewHelpers.SecondaryButton("解除");
        public TextBlock InvoiceLabel { get; set; } = new();
        public TextBox InvoiceNumber { get; } = new();
        public TextBlock InvoiceInfo { get; } = new()
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#4A5568")
        };
        public TextBox Description { get; } = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };

        public void SetSources(
            IReadOnlyList<Account> accounts,
            IReadOnlyList<TaxCode> taxCodes,
            IReadOnlyList<BusinessPartner> partners,
            IReadOnlyList<TaxInputOption> taxInputOptions,
            int defaultTaxInputTypeIndex)
        {
            DebitAccount.ItemTemplate = AccountTemplate;
            DebitAccount.ItemsSource = accounts;
            CreditAccount.ItemTemplate = AccountTemplate;
            CreditAccount.ItemsSource = accounts;
            DebitSubAccount.ItemTemplate = SubAccountTemplate;
            DebitSubAccount.ItemsSource = Array.Empty<SubAccount>();
            CreditSubAccount.ItemTemplate = SubAccountTemplate;
            CreditSubAccount.ItemsSource = Array.Empty<SubAccount>();
            DebitTaxCode.ItemTemplate = TaxCodeTemplate;
            DebitTaxCode.ItemsSource = taxCodes;
            CreditTaxCode.ItemTemplate = TaxCodeTemplate;
            CreditTaxCode.ItemsSource = taxCodes;
            Partner.ItemTemplate = PartnerTemplate;
            Partner.ItemsSource = partners;
            Partner.SelectedIndex = -1;
            DebitTaxInputType.ItemTemplate = TaxInputTemplate;
            DebitTaxInputType.ItemsSource = taxInputOptions;
            CreditTaxInputType.ItemTemplate = TaxInputTemplate;
            CreditTaxInputType.ItemsSource = taxInputOptions;
            DebitTaxInputType.SelectedIndex = defaultTaxInputTypeIndex;
            CreditTaxInputType.SelectedIndex = defaultTaxInputTypeIndex;
            if (taxCodes.Count > 0)
            {
                DebitTaxCode.SelectedIndex = 0;
                CreditTaxCode.SelectedIndex = 0;
            }
        }

        private static readonly IDataTemplate AccountTemplate = new FuncDataTemplate<Account>((account, _) =>
            new TextBlock
            {
                Text = account is null ? "" : $"{account.Code} {account.Name}",
                Foreground = Brush.Parse("#243044")
            });

        private static readonly IDataTemplate SubAccountTemplate = new FuncDataTemplate<SubAccount>((subAccount, _) =>
            new TextBlock
            {
                Text = subAccount is null ? "" : $"{subAccount.Code} {subAccount.Name}",
                Foreground = Brush.Parse("#243044")
            });

        private static readonly IDataTemplate TaxCodeTemplate = new FuncDataTemplate<TaxCode>((taxCode, _) =>
            new TextBlock
            {
                Text = taxCode is null ? "" : $"{taxCode.Code} {taxCode.Name}",
                Foreground = Brush.Parse("#243044")
            });

        private static readonly IDataTemplate PartnerTemplate = new FuncDataTemplate<BusinessPartner>((partner, _) =>
            new TextBlock
            {
                Text = partner is null ? "" : $"{partner.Code} {partner.Name}",
                Foreground = Brush.Parse("#243044")
            });

        private static readonly IDataTemplate TaxInputTemplate = new FuncDataTemplate<TaxInputOption>((option, _) =>
            new TextBlock
            {
                Text = option?.Label ?? "",
                Foreground = Brush.Parse("#243044")
            });
    }

    private sealed record TaxInputOption(string Value, string Label);
}
