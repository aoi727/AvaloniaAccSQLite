using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class LoginView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly Action<AppUser> _signedIn;
    private TextBox _loginId = null!;
    private TextBox _password = null!;
    private TextBlock _message = null!;
    private Button _signInButton = null!;
    private Button _initSchemaButton = null!;
    private Button _createAdminButton = null!;

    public LoginView(SqliteDatabase database, Action<AppUser> signedIn)
    {
        _database = database;
        _signedIn = signedIn;
        Content = Build();
        _ = CheckDatabaseAsync();
    }

    private Control Build()
    {
        _loginId = new TextBox { PlaceholderText = "admin" };
        _password = new TextBox { PlaceholderText = "password", PasswordChar = '*' };
        _message = ViewHelpers.Body("");
        _signInButton = ViewHelpers.PrimaryButton("ログイン");
        _initSchemaButton = ViewHelpers.SecondaryButton("DBスキーマを初期化");
        _createAdminButton = ViewHelpers.SecondaryButton("初期管理者を作成");

        _signInButton.Click += async (_, _) => await SignInAsync();
        _initSchemaButton.Click += async (_, _) => await InitializeSchemaAsync();
        _createAdminButton.Click += (_, _) => ShowCreateAdmin();

        var form = new StackPanel
        {
            Width = 420,
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("ログイン"),
                ViewHelpers.Body("会社を選ぶ前に、ユーザー認証で作業を始めます。"),
                ViewHelpers.Label("ログインID"),
                _loginId,
                ViewHelpers.Label("パスワード"),
                _password,
                new Border { Height = 8 },
                _signInButton,
                _createAdminButton,
                _initSchemaButton,
                _message
            }
        };

        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Children =
            {
                Place(ViewHelpers.Panel(form), 1, 1)
            }
        };
    }

    private static Control Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private async Task CheckDatabaseAsync()
    {
        SetMessage("SQLite 接続を確認しています。", false);
        if (!await _database.CanConnectAsync())
        {
            SetMessage("DBに接続できません。接続文字列を確認してください。必要ならスキーマ初期化の前にDBを作成してください。", true);
            return;
        }

        try
        {
            var hasUsers = await _database.HasUsersAsync();
            SetMessage(hasUsers ? "接続できました。ログインできます。" : "接続できました。初期管理者を作成してください。", false);
        }
        catch
        {
            SetMessage("接続できました。テーブル未作成の場合は「DBスキーマを初期化」を押してください。", false);
        }
    }

    private async Task InitializeSchemaAsync()
    {
        await RunBusyAsync(_initSchemaButton, async () =>
        {
            await _database.InitializeSchemaAsync();
            SetMessage("DBスキーマを初期化しました。初期管理者を作成できます。", false);
        });
    }

    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(_loginId.Text) || string.IsNullOrWhiteSpace(_password.Text))
        {
            SetMessage("ログインIDとパスワードを入力してください。", true);
            return;
        }

        await RunBusyAsync(_signInButton, async () =>
        {
            var user = await _database.AuthenticateAsync(_loginId.Text.Trim(), _password.Text);
            if (user is null)
            {
                SetMessage("ログインIDまたはパスワードが違います。", true);
                return;
            }

            _signedIn(user);
        });
    }

    private void ShowCreateAdmin()
    {
        Content = new CreateAdminView(_database, _signedIn, () => Content = Build());
    }

    private async Task RunBusyAsync(Button button, Func<Task> action)
    {
        try
        {
            button.IsEnabled = false;
            await action();
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

    private void SetMessage(string text, bool isError)
    {
        _message.Text = text;
        _message.Foreground = isError ? Brush.Parse("#B42318") : Brush.Parse("#4A5568");
    }
}
