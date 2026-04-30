using System.Globalization;
using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Views;

public sealed class SubAccountFormView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly Action? _backToAccountForm;
    private readonly int? _initialAccountId;
    private readonly ComboBox _account = new();
    private readonly TextBox _code = new() { PlaceholderText = "例: YOKOHAMA-001 / CUST-001" };
    private readonly TextBox _name = new() { PlaceholderText = "例: 横浜営業所 / 得意先 1234567" };
    private readonly TextBox _externalCode = new() { PlaceholderText = "外部システム連携コードなど" };
    private readonly TextBox _balance = new() { Text = "0" };
    private readonly StackPanel _subAccounts = new() { Spacing = 8 };
    private readonly TextBlock _message = ViewHelpers.Body("補助科目を登録できます。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("登録する");
    private readonly Button _newButton = ViewHelpers.SecondaryButton("新規に戻す");
    private int? _editingSubAccountId;

    public SubAccountFormView(
        SqliteDatabase database,
        AppUser user,
        Action backToDashboard,
        Action? backToAccountForm = null,
        int? initialAccountId = null)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _backToAccountForm = backToAccountForm;
        _initialAccountId = initialAccountId;
        Content = Build();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _newButton.Click += (_, _) => ClearForm();
        _account.SelectionChanged += async (_, _) => await LoadSubAccountsAsync();
        _ = LoadAsync();
    }

    private Control Build()
    {
        var navigationButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var backToDashboardButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backToDashboardButton.Width = 140;
        backToDashboardButton.HorizontalAlignment = HorizontalAlignment.Left;
        backToDashboardButton.Click += (_, _) => _backToDashboard();
        navigationButtons.Children.Add(backToDashboardButton);

        if (_backToAccountForm is not null)
        {
            var backToAccountButton = ViewHelpers.SecondaryButton("科目登録へ戻る");
            backToAccountButton.Width = 160;
            backToAccountButton.HorizontalAlignment = HorizontalAlignment.Left;
            backToAccountButton.Click += (_, _) => _backToAccountForm();
            navigationButtons.Children.Add(backToAccountButton);
        }

        var form = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("補助科目登録"),
                ViewHelpers.Body("外部管理コードや残高などを主科目に紐づけて登録します。"),
                ViewHelpers.Label("主科目"),
                _account,
                ViewHelpers.Label("補助コード"),
                _code,
                ViewHelpers.Label("補助科目名"),
                _name,
                ViewHelpers.Label("外部コード"),
                _externalCode,
                ViewHelpers.Label("開始残高"),
                _balance,
                new Border { Height = 8 },
                _saveButton,
                _newButton,
                _message
            }
        });

        var content = new Grid
        {
            Margin = new Thickness(28),
            ColumnDefinitions = new ColumnDefinitions("420,24,*"),
            RowDefinitions = new RowDefinitions("Auto,18,*"),
            Children =
            {
                Header(navigationButtons),
                form,
                ExistingList()
            }
        };

        Grid.SetRow(form, 2);
        Grid.SetColumn(form, 0);

        return new ScrollViewer { Content = content };
    }

    private Control Header(Control navigationButtons)
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
                        ViewHelpers.Body("補助科目マスタ")
                    }
                },
                navigationButtons
            }
        };
        Grid.SetColumn(navigationButtons, 1);
        Grid.SetColumnSpan(header, 3);
        return header;
    }

    private Control ExistingList()
    {
        var panel = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                ViewHelpers.Heading("選択中の主科目の補助科目", 20),
                _subAccounts
            }
        });
        Grid.SetRow(panel, 2);
        Grid.SetColumn(panel, 2);
        return panel;
    }

    private async Task LoadAsync()
    {
        try
        {
            var accounts = await _database.GetControlAccountsAsync(_user.CompanyId);
            _account.ItemsSource = accounts;
            if (accounts.Count > 0)
            {
                SelectInitialAccount(accounts);
                _saveButton.IsEnabled = true;
            }
            else
            {
                _saveButton.IsEnabled = false;
                SetMessage("補助科目を持てる主科目がありません。勘定科目の is_control_account を確認してください。", true);
            }

            await LoadSubAccountsAsync();
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
    }

    private async Task LoadSubAccountsAsync()
    {
        if (_account.SelectedItem is not Account selectedAccount)
        {
            _subAccounts.Children.Clear();
            _subAccounts.Children.Add(ViewHelpers.Body("主科目を選択してください。"));
            return;
        }

        var subAccounts = await _database.GetSubAccountsAsync(_user.CompanyId, selectedAccount.AccountId);
        _subAccounts.Children.Clear();

        if (subAccounts.Count == 0)
        {
            _subAccounts.Children.Add(ViewHelpers.Body($"{selectedAccount.Code} {selectedAccount.Name} には、まだ補助科目が登録されていません。"));
            return;
        }

        foreach (var subAccount in subAccounts)
        {
            _subAccounts.Children.Add(SubAccountRow(subAccount));
        }
    }

    private async Task SaveAsync()
    {
        if (_account.SelectedItem is not Account selectedAccount)
        {
            SetMessage("主科目を選択してください。", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text))
        {
            SetMessage("補助コードと補助科目名を入力してください。", true);
            return;
        }

        if (!decimal.TryParse(_balance.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var balance))
        {
            SetMessage("開始残高は数値で入力してください。", true);
            return;
        }

        try
        {
            _saveButton.IsEnabled = false;
            if (_editingSubAccountId.HasValue)
            {
                await _database.UpdateSubAccountAsync(
                    _user.CompanyId,
                    _editingSubAccountId.Value,
                    selectedAccount.AccountId,
                    _code.Text.Trim(),
                    _name.Text.Trim(),
                    _externalCode.Text,
                    balance,
                    true);

                ClearForm();
                SetMessage("補助科目を更新しました。", false);
                await LoadSubAccountsAsync();
                return;
            }

            await _database.CreateSubAccountAsync(
                _user.CompanyId,
                selectedAccount.AccountId,
                _code.Text.Trim(),
                _name.Text.Trim(),
                _externalCode.Text,
                balance);

            _code.Text = "";
            _name.Text = "";
            _externalCode.Text = "";
            _balance.Text = "0";
            SetMessage("補助科目を登録しました。", false);
            await LoadSubAccountsAsync();
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            SetMessage("同じ主科目に同じ補助コードが既に登録されています。", true);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            _saveButton.IsEnabled = _account.SelectedItem is Account;
        }
    }

    private Control SubAccountRow(SubAccount subAccount)
    {
        var editButton = ViewHelpers.SecondaryButton("編集");
        editButton.Width = 80;
        editButton.Click += (_, _) => LoadForEdit(subAccount);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,130,*,120,90"),
            Children =
            {
                Cell($"{subAccount.AccountCode} {subAccount.AccountName}", 0, FontWeight.SemiBold),
                Cell(subAccount.Code, 1),
                Cell(subAccount.Name, 2),
                Cell(subAccount.Balance.ToString("N0"), 3),
                editButton
            }
        };
        Grid.SetColumn(editButton, 4);

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

    private void LoadForEdit(SubAccount subAccount)
    {
        _editingSubAccountId = subAccount.SubAccountId;
        SelectAccount(subAccount.AccountId);
        _code.Text = subAccount.Code;
        _name.Text = subAccount.Name;
        _externalCode.Text = subAccount.ExternalCode ?? "";
        _balance.Text = subAccount.Balance.ToString("0.##");
        _saveButton.Content = "更新する";
        SetMessage($"{subAccount.Code} {subAccount.Name} を編集中です。", false);
    }

    private void ClearForm()
    {
        _editingSubAccountId = null;
        _code.Text = "";
        _name.Text = "";
        _externalCode.Text = "";
        _balance.Text = "0";
        _saveButton.Content = "登録する";
    }

    private void SelectAccount(int accountId)
    {
        if (_account.ItemsSource is not IEnumerable<Account> accounts)
        {
            return;
        }

        var account = accounts.FirstOrDefault(x => x.AccountId == accountId);
        if (account is not null)
        {
            _account.SelectedItem = account;
        }
    }

    private void SelectInitialAccount(IReadOnlyList<Account> accounts)
    {
        if (_initialAccountId.HasValue)
        {
            var initial = accounts.FirstOrDefault(x => x.AccountId == _initialAccountId.Value);
            if (initial is not null)
            {
                _account.SelectedItem = initial;
                return;
            }
        }

        _account.SelectedIndex = 0;
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

    private void SetMessage(string text, bool isError)
    {
        _message.Text = text;
        _message.Foreground = isError ? Brush.Parse("#B42318") : Brush.Parse("#4A5568");
    }
}
