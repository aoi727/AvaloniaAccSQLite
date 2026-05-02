using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AccountingApp.Views;

public sealed class CreateAdminView : UserControl
{
    private sealed record ClosingDayOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly SqliteDatabase _database;
    private readonly Action<AppUser> _created;
    private readonly Action _cancel;
    private readonly TextBox _companyName = new() { Text = "サンプル株式会社" };
    private readonly DatePicker _fiscalYearStart = new() { SelectedDate = new DateTimeOffset(GetDefaultFiscalYearStart(DateTime.Today)) };
    private readonly ComboBox _closingDay = new() { ItemsSource = CreateClosingDayOptions(), SelectedIndex = 30 };
    private readonly CheckBox _isTaxExempt = new() { Content = "免税事業者" };
    private readonly RadioButton _presetAccountsOption = new() { Content = "プリセットを使う", GroupName = "account-source", IsChecked = true };
    private readonly RadioButton _csvAccountsOption = new() { Content = "CSVファイルから設定する", GroupName = "account-source" };
    private readonly Button _selectAccountsCsvButton = ViewHelpers.SecondaryButton("勘定科目CSVを選択");
    private readonly TextBlock _selectedAccountsCsvPath = ViewHelpers.Body("CSVファイルは未選択です。");
    private readonly TextBox _loginId = new() { Text = "admin" };
    private readonly TextBox _displayName = new() { Text = "管理者" };
    private readonly TextBox _password = new() { Text = "password", PasswordChar = '*' };
    private readonly TextBox _passwordConfirm = new() { Text = "password", PasswordChar = '*' };
    private readonly CheckBox _showPassword = new() { Content = "パスワードを表示" };
    private readonly TextBlock _message = ViewHelpers.Body("");
    private readonly Button _createButton = ViewHelpers.PrimaryButton("作成してログイン");
    private string? _accountSeedCsvPath;

    public CreateAdminView(SqliteDatabase database, Action<AppUser> created, Action cancel)
    {
        _database = database;
        _created = created;
        _cancel = cancel;
        Content = Build();
        _createButton.Click += async (_, _) => await CreateAsync();
        _showPassword.IsCheckedChanged += (_, _) => UpdatePasswordVisibility();
        _presetAccountsOption.IsCheckedChanged += (_, _) => UpdateAccountSourceState();
        _csvAccountsOption.IsCheckedChanged += (_, _) => UpdateAccountSourceState();
        _selectAccountsCsvButton.Click += async (_, _) => await SelectAccountSeedCsvAsync();
        UpdateAccountSourceState();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ログインに戻る");
        backButton.Click += (_, _) => _cancel();

        var form = new StackPanel
        {
            Width = 460,
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("初期管理者の作成"),
                ViewHelpers.Body("最初の会社と管理者ユーザーを作成します。税区分と勘定科目も合わせて初期設定します。"),
                ViewHelpers.Label("会社名"),
                _companyName,
                ViewHelpers.Label("会計年度開始日"),
                _fiscalYearStart,
                ViewHelpers.Label("締め日"),
                _closingDay,
                _isTaxExempt,
                ViewHelpers.Label("勘定科目の初期設定"),
                _presetAccountsOption,
                _csvAccountsOption,
                _selectAccountsCsvButton,
                _selectedAccountsCsvPath,
                ViewHelpers.Body("CSVは Database/seed_accounts.csv と同じ列構成を想定しています。code、name、account_type、is_control_account、default_tax_code_id、balance_side を使用します。"),
                ViewHelpers.Body("初期設定では補助科目コード 0 を自動作成します。"),
                ViewHelpers.Label("ログインID"),
                _loginId,
                ViewHelpers.Label("表示名"),
                _displayName,
                ViewHelpers.Label("パスワード"),
                _password,
                ViewHelpers.Label("パスワード確認"),
                _passwordConfirm,
                _showPassword,
                new Border { Height = 8 },
                _createButton,
                backButton,
                _message
            }
        };

        var panel = ViewHelpers.Panel(form);
        Grid.SetRow(panel, 1);
        Grid.SetColumn(panel, 1);

        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Children = { panel }
        };
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_companyName.Text) ||
            string.IsNullOrWhiteSpace(_loginId.Text) ||
            string.IsNullOrWhiteSpace(_displayName.Text) ||
            string.IsNullOrWhiteSpace(_password.Text) ||
            string.IsNullOrWhiteSpace(_passwordConfirm.Text))
        {
            SetMessage("すべての必須項目を入力してください。", true);
            return;
        }

        if (!string.Equals(_password.Text, _passwordConfirm.Text, StringComparison.Ordinal))
        {
            SetMessage("パスワードと確認用パスワードが一致しません。", true);
            return;
        }

        if (_csvAccountsOption.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(_accountSeedCsvPath))
            {
                SetMessage("勘定科目CSVを選択してください。", true);
                return;
            }

            if (!File.Exists(_accountSeedCsvPath))
            {
                SetMessage("選択された勘定科目CSVが見つかりません。", true);
                return;
            }
        }

        if (_password.Text.Length < 8)
        {
            SetMessage("パスワードは8文字以上にしてください。", true);
            return;
        }

        try
        {
            _createButton.IsEnabled = false;
            var selectedDate = _fiscalYearStart.SelectedDate?.DateTime.Date ?? GetDefaultFiscalYearStart(DateTime.Today);
            var closingDay = _closingDay.SelectedItem is ClosingDayOption selectedClosingDay ? selectedClosingDay.Value : 31;
            var user = await _database.CreateInitialAdminAsync(
                _companyName.Text.Trim(),
                selectedDate,
                closingDay,
                _loginId.Text.Trim(),
                _displayName.Text.Trim(),
                _password.Text,
                isTaxExempt: _isTaxExempt.IsChecked == true,
                accountSeedCsvPath: _csvAccountsOption.IsChecked == true ? _accountSeedCsvPath : null);
            _created(user);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            _createButton.IsEnabled = true;
        }
    }

    private async Task SelectAccountSeedCsvAsync()
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
                    MimeTypes = ["text/csv", "application/csv", "application/vnd.ms-excel", "text/plain"]
                },
                FilePickerFileTypes.All
            ]
        });

        var selectedPath = files.Count > 0 ? files[0].Path.LocalPath : null;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        _accountSeedCsvPath = selectedPath;
        _selectedAccountsCsvPath.Text = selectedPath;
        SetMessage("勘定科目CSVを設定しました。", false);
    }

    private void SetMessage(string text, bool isError)
    {
        _message.Text = text;
        _message.Foreground = isError ? Brush.Parse("#B42318") : Brush.Parse("#4A5568");
    }

    private void UpdatePasswordVisibility()
    {
        var show = _showPassword.IsChecked == true;
        _password.PasswordChar = show ? '\0' : '*';
        _passwordConfirm.PasswordChar = show ? '\0' : '*';
    }

    private void UpdateAccountSourceState()
    {
        var usesCsv = _csvAccountsOption.IsChecked == true;
        _selectAccountsCsvButton.IsEnabled = usesCsv;
        _selectedAccountsCsvPath.IsVisible = usesCsv;
    }

    private static IReadOnlyList<ClosingDayOption> CreateClosingDayOptions()
    {
        var options = Enumerable.Range(1, 30)
            .Select(day => new ClosingDayOption(day, $"{day}日"))
            .ToList();
        options.Add(new ClosingDayOption(31, "末日"));
        return options;
    }

    private static DateTime GetDefaultFiscalYearStart(DateTime referenceDate)
    {
        return new DateTime(referenceDate.Year, 1, 1);
    }
}
