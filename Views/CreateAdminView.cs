using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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
    private readonly TextBox _loginId = new() { Text = "admin" };
    private readonly TextBox _displayName = new() { Text = "管理者" };
    private readonly TextBox _password = new() { Text = "password", PasswordChar = '*' };
    private readonly TextBox _passwordConfirm = new() { Text = "password", PasswordChar = '*' };
    private readonly CheckBox _showPassword = new() { Content = "パスワードを表示" };
    private readonly TextBlock _message = ViewHelpers.Body("");
    private readonly Button _createButton = ViewHelpers.PrimaryButton("作成してログイン");

    public CreateAdminView(SqliteDatabase database, Action<AppUser> created, Action cancel)
    {
        _database = database;
        _created = created;
        _cancel = cancel;
        Content = Build();
        _createButton.Click += async (_, _) => await CreateAsync();
        _showPassword.IsCheckedChanged += (_, _) => UpdatePasswordVisibility();
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
                ViewHelpers.Body("最初の会社と管理者ユーザーを作成します。免税事業者を選ぶと総額方式で開始します。"),
                ViewHelpers.Label("会社名"),
                _companyName,
                ViewHelpers.Label("会計年度期首日"),
                _fiscalYearStart,
                ViewHelpers.Label("締め日"),
                _closingDay,
                _isTaxExempt,
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
            SetMessage("すべての項目を入力してください。", true);
            return;
        }

        if (!string.Equals(_password.Text, _passwordConfirm.Text, StringComparison.Ordinal))
        {
            SetMessage("パスワードと確認用パスワードが一致しません。", true);
            return;
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
                isTaxExempt: _isTaxExempt.IsChecked == true);
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
