using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Data.Sqlite;
using System.Text;

namespace AccountingApp.Views;

public sealed class AccountFormView : UserControl
{
    private static readonly string[] AccountTypes = ["asset", "liability", "equity", "revenue", "expense"];
    private static readonly string[] BalanceSides = ["debit", "credit"];

    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly Action<int?, bool> _openSubAccountForm;
    private readonly TextBox _code = new() { PlaceholderText = "例: 1120" };
    private readonly TextBox _name = new() { PlaceholderText = "例: 普通預金" };
    private readonly ComboBox _accountType = new() { ItemsSource = AccountTypes, SelectedIndex = 0 };
    private readonly ComboBox _balanceSide = new() { ItemsSource = BalanceSides, SelectedIndex = 0 };
    private readonly ComboBox _defaultTaxCode = new();
    private readonly CheckBox _isControlAccount = new() { Content = "補助科目あり" };
    private readonly StackPanel _accounts = new() { Spacing = 8 };
    private readonly TextBlock _message = ViewHelpers.Body("勘定科目を登録・編集できます。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("登録する");
    private readonly Button _newButton = ViewHelpers.SecondaryButton("新規に戻す");
    private readonly Button _importCsvButton = ViewHelpers.SecondaryButton("CSV取込");
    private readonly Button _exportCsvButton = ViewHelpers.SecondaryButton("CSV保存");
    private readonly List<TaxCode> _taxCodes = [];
    private int? _editingAccountId;

    public AccountFormView(SqliteDatabase database, AppUser user, Action backToDashboard, Action<int?, bool> openSubAccountForm)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _openSubAccountForm = openSubAccountForm;
        Content = Build();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _newButton.Click += (_, _) => ClearForm();
        _importCsvButton.Click += async (_, _) => await ImportCsvAsync();
        _exportCsvButton.Click += async (_, _) => await ExportCsvAsync();
        _accountType.SelectionChanged += (_, _) => ApplySuggestedBalanceSide();
        _ = LoadAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.HorizontalAlignment = HorizontalAlignment.Left;
        backButton.Click += (_, _) => _backToDashboard();

        var openSubAccountButton = ViewHelpers.SecondaryButton("補助科目管理へ");
        openSubAccountButton.Width = 180;
        openSubAccountButton.HorizontalAlignment = HorizontalAlignment.Left;
        openSubAccountButton.Click += (_, _) => _openSubAccountForm(_editingAccountId, true);

        var form = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("勘定科目登録"),
                ViewHelpers.Body("科目コード、名称、区分、残高性質、既定税区分、補助科目の有無を管理します。"),
                ViewHelpers.Label("勘定科目コード"),
                _code,
                ViewHelpers.Label("勘定科目名"),
                _name,
                ViewHelpers.Label("勘定科目区分"),
                _accountType,
                ViewHelpers.Label("残高性質"),
                _balanceSide,
                ViewHelpers.Label("既定税区分"),
                _defaultTaxCode,
                new Border { Height = 8 },
                _isControlAccount,
                new Border { Height = 8 },
                _saveButton,
                _newButton,
                _importCsvButton,
                _exportCsvButton,
                openSubAccountButton,
                _message
            }
        });

        var content = new Grid
        {
            Margin = new Thickness(28),
            ColumnDefinitions = new ColumnDefinitions("420,24,*"),
            RowDefinitions = new RowDefinitions("Auto,18,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                Header(backButton),
                form,
                ExistingList()
            }
        };

        Grid.SetRow(form, 2);
        Grid.SetColumn(form, 0);

        return content;
    }

    private Control Header(Control backButton)
    {
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
                        ViewHelpers.Body("勘定科目マスタ")
                    }
                },
                backButton
            }
        };
        Grid.SetColumn(backButton, 1);
        Grid.SetColumnSpan(header, 3);
        return header;
    }

    private Control ExistingList()
    {
        var listScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _accounts
        };
        Grid.SetRow(listScroll, 2);

        var listGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,12,*"),
            Children =
            {
                ViewHelpers.Heading("登録済み勘定科目", 20),
                listScroll
            }
        };

        var panel = ViewHelpers.Panel(listGrid);
        Grid.SetRow(panel, 2);
        Grid.SetColumn(panel, 2);
        return panel;
    }

    private async Task LoadAsync()
    {
        try
        {
            _taxCodes.Clear();
            _taxCodes.AddRange(await _database.GetTaxCodesAsync(_user.CompanyId));
            if (_taxCodes.Count == 0)
            {
                await _database.EnsureDefaultTaxCodesAsync(_user.CompanyId);
                _taxCodes.AddRange(await _database.GetTaxCodesAsync(_user.CompanyId));
            }

            var taxChoices = new List<DefaultTaxCodeChoice>
            {
                new(null, "未設定")
            };
            taxChoices.AddRange(_taxCodes.Select(x => new DefaultTaxCodeChoice(x.TaxCodeId, $"{x.Code} {x}")));
            _defaultTaxCode.ItemsSource = taxChoices;
            _defaultTaxCode.SelectedIndex = 0;

            ClearForm();
            await LoadAccountsAsync();
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
    }

    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _database.GetAccountsAsync(_user.CompanyId, includeInactive: true);
            _accounts.Children.Clear();

            if (accounts.Count == 0)
            {
                _accounts.Children.Add(ViewHelpers.Body("まだ登録されていません。"));
                return;
            }

            foreach (var account in accounts)
            {
                _accounts.Children.Add(AccountRowWithVisibility(account));
            }
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
    }

    private Control AccountRow(Account account)
    {
        var editButton = ViewHelpers.SecondaryButton("編集");
        editButton.Width = 80;
        editButton.Click += (_, _) => LoadForEdit(account);

        var subAccountButton = ViewHelpers.SecondaryButton("補助科目");
        subAccountButton.Width = 110;
        subAccountButton.Click += (_, _) => _openSubAccountForm(account.AccountId, true);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,120,90,170,110,110,90"),
            Children =
            {
                Cell(account.Code, 0, FontWeight.SemiBold),
                Cell(account.Name, 1),
                Cell(ToAccountTypeLabel(account.AccountType), 2),
                Cell(ToBalanceSideLabel(account.BalanceSide), 3),
                Cell(ToTaxCodeLabel(account.DefaultTaxCodeId), 4),
                Cell(account.IsControlAccount ? "補助あり" : "補助なし", 5),
                subAccountButton,
                editButton
            }
        };
        Grid.SetColumn(subAccountButton, 6);
        Grid.SetColumn(editButton, 7);

        return new Border
        {
            Background = Brush.Parse("#F8FAFC"),
            BorderBrush = Brush.Parse("#E2E8F0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Child = row
        };
    }

    private Control AccountRowWithVisibility(Account account)
    {
        var editButton = ViewHelpers.SecondaryButton("編集");
        editButton.Width = 80;
        editButton.Click += (_, _) => LoadForEdit(account);

        var subAccountButton = ViewHelpers.SecondaryButton("補助科目");
        subAccountButton.Width = 110;
        subAccountButton.IsEnabled = account.IsActive;
        subAccountButton.Click += (_, _) => _openSubAccountForm(account.AccountId, true);

        var activeButton = ViewHelpers.SecondaryButton(account.IsActive ? "非表示" : "再表示");
        activeButton.Width = 90;
        activeButton.Click += async (_, _) => await ToggleAccountActiveAsync(account, activeButton);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,120,90,170,80,110,110,90,90"),
            Children =
            {
                Cell(account.Code, 0, FontWeight.SemiBold),
                Cell(account.Name, 1),
                Cell(ToAccountTypeLabel(account.AccountType), 2),
                Cell(ToBalanceSideLabel(account.BalanceSide), 3),
                Cell(ToTaxCodeLabel(account.DefaultTaxCodeId), 4),
                Cell(account.IsActive ? "表示" : "非表示", 5),
                Cell(account.IsControlAccount ? "補助あり" : "補助なし", 6),
                subAccountButton,
                editButton,
                activeButton
            }
        };
        Grid.SetColumn(subAccountButton, 7);
        Grid.SetColumn(editButton, 8);
        Grid.SetColumn(activeButton, 9);

        return new Border
        {
            Background = account.IsActive ? Brush.Parse("#F8FAFC") : Brush.Parse("#F1F5F9"),
            BorderBrush = Brush.Parse("#E2E8F0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Opacity = account.IsActive ? 1 : 0.72,
            Padding = new Thickness(12, 10),
            Child = row
        };
    }

    private async Task ToggleAccountActiveAsync(Account account, Button button)
    {
        try
        {
            button.IsEnabled = false;
            await _database.SetAccountActiveAsync(_user.CompanyId, account.AccountId, !account.IsActive);
            SetMessage(account.IsActive
                ? $"{account.Code} {account.Name} を非表示にしました。"
                : $"{account.Code} {account.Name} を表示に戻しました。", false);
            await LoadAccountsAsync();
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void LoadForEdit(Account account)
    {
        _editingAccountId = account.AccountId;
        _code.Text = account.Code;
        _name.Text = account.Name;
        _accountType.SelectedItem = account.AccountType;
        _balanceSide.SelectedItem = account.BalanceSide;
        SelectDefaultTaxCode(account.DefaultTaxCodeId);
        _isControlAccount.IsChecked = account.IsControlAccount;
        _saveButton.Content = "更新する";
        SetMessage($"{account.Code} {account.Name} を編集中です。", false);
    }

    private void ClearForm()
    {
        _editingAccountId = null;
        _code.Text = "";
        _name.Text = "";
        _accountType.SelectedIndex = 0;
        _defaultTaxCode.SelectedIndex = 0;
        _isControlAccount.IsChecked = false;
        _saveButton.Content = "登録する";
        ApplySuggestedBalanceSide();
        SetMessage("新しい勘定科目を入力できます。", false);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text))
        {
            SetMessage("勘定科目コードと勘定科目名を入力してください。", true);
            return;
        }

        if (_accountType.SelectedItem is not string selectedType)
        {
            SetMessage("勘定科目区分を選択してください。", true);
            return;
        }

        if (_balanceSide.SelectedItem is not string selectedBalanceSide)
        {
            SetMessage("残高性質を選択してください。", true);
            return;
        }

        try
        {
            _saveButton.IsEnabled = false;
            var defaultTaxCodeId = _defaultTaxCode.SelectedItem is DefaultTaxCodeChoice choice
                ? choice.TaxCodeId
                : null;

            if (_editingAccountId.HasValue)
            {
                await _database.UpdateAccountAsync(
                    _user.CompanyId,
                    _editingAccountId.Value,
                    _code.Text.Trim(),
                    _name.Text.Trim(),
                    selectedType,
                    selectedBalanceSide,
                    _isControlAccount.IsChecked == true,
                    defaultTaxCodeId);
                SetMessage("勘定科目を更新しました。", false);
            }
            else
            {
                await _database.CreateAccountAsync(
                    _user.CompanyId,
                    _code.Text.Trim(),
                    _name.Text.Trim(),
                    selectedType,
                    selectedBalanceSide,
                    _isControlAccount.IsChecked == true,
                    defaultTaxCodeId);
                SetMessage("勘定科目を登録しました。", false);
                ClearForm();
            }

            await LoadAccountsAsync();
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            SetMessage("同じ勘定科目コードが既に登録されています。", true);
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 275)
        {
            SetMessage("勘定科目区分または残高性質が不正です。", true);
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

    private async Task ExportCsvAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            SetMessage("保存ダイアログを開けませんでした。", true);
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "勘定科目CSVを保存",
            SuggestedFileName = "accounts.csv",
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
            var rows = await _database.GetAccountCsvRowsAsync(_user.CompanyId);
            var csv = AccountCsvSerializer.Serialize(rows);
            await File.WriteAllTextAsync(file.Path.LocalPath, csv, new UTF8Encoding(false));
            SetMessage($"CSVを保存しました: {file.Name}", false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
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
            SetMessage("ファイル選択を開けませんでした。", true);
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "勘定科目CSVを選択",
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
            var rows = AccountCsvSerializer.Deserialize(csvText);
            await _database.ImportAccountCsvAsync(_user.CompanyId, rows);
            await LoadAsync();
            SetMessage($"CSVを取り込みました: {Path.GetFileName(path)}", false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            _importCsvButton.IsEnabled = true;
        }
    }

    private static Control Cell(string text, int column, FontWeight weight = default)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = weight == default ? FontWeight.Normal : weight,
            Foreground = Brush.Parse("#243044"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private void SelectDefaultTaxCode(int? defaultTaxCodeId)
    {
        if (_defaultTaxCode.ItemsSource is not IEnumerable<DefaultTaxCodeChoice> choices)
        {
            _defaultTaxCode.SelectedIndex = 0;
            return;
        }

        var selected = choices.FirstOrDefault(x => x.TaxCodeId == defaultTaxCodeId);
        _defaultTaxCode.SelectedItem = selected ?? choices.FirstOrDefault();
    }

    private string ToTaxCodeLabel(int? defaultTaxCodeId)
    {
        if (!defaultTaxCodeId.HasValue)
        {
            return "未設定";
        }

        var taxCode = _taxCodes.FirstOrDefault(x => x.TaxCodeId == defaultTaxCodeId.Value);
        return taxCode is null ? "未設定" : $"{taxCode.Code} {taxCode}";
    }

    private static string ToAccountTypeLabel(string accountType)
    {
        return accountType switch
        {
            "asset" => "資産",
            "liability" => "負債",
            "equity" => "純資産",
            "revenue" => "収益",
            "expense" => "費用",
            _ => accountType
        };
    }

    private static string ToBalanceSideLabel(string balanceSide)
    {
        return balanceSide switch
        {
            "debit" => "借方",
            "credit" => "貸方",
            _ => balanceSide
        };
    }

    private void ApplySuggestedBalanceSide()
    {
        if (_editingAccountId.HasValue)
        {
            return;
        }

        if (_accountType.SelectedItem is not string accountType)
        {
            return;
        }

        _balanceSide.SelectedItem = accountType is "asset" or "expense" ? "debit" : "credit";
    }

    private void SetMessage(string text, bool isError)
    {
        _message.Text = text;
        _message.Foreground = isError ? Brush.Parse("#B42318") : Brush.Parse("#4A5568");
    }

    private sealed record DefaultTaxCodeChoice(int? TaxCodeId, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
