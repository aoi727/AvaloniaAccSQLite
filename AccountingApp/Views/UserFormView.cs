using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Views;

public sealed class UserFormView : UserControl
{
    private static readonly string[] Roles = ["admin", "user"];

    private readonly SqliteDatabase _database;
    private readonly AppUser _currentUser;
    private readonly Action _backToDashboard;
    private readonly TextBox _loginId = new() { PlaceholderText = "例: yamada" };
    private readonly TextBox _displayName = new() { PlaceholderText = "例: 山田 太郎" };
    private readonly TextBox _currentPassword = new() { PlaceholderText = "パスワード変更時のみ入力", PasswordChar = '*' };
    private readonly TextBox _password = new() { PlaceholderText = "新しいパスワード", PasswordChar = '*' };
    private readonly TextBox _passwordConfirm = new() { PlaceholderText = "新しいパスワード(確認)", PasswordChar = '*' };
    private readonly CheckBox _showPassword = new() { Content = "パスワードを表示" };
    private readonly ComboBox _role = new() { ItemsSource = Roles, SelectedIndex = 1 };
    private readonly CheckBox _isActive = new() { Content = "有効", IsChecked = true };
    private readonly StackPanel _users = new() { Spacing = 8 };
    private readonly TextBlock _message = ViewHelpers.Body("ユーザーを追加・編集できます。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("登録する");
    private readonly Button _newButton = ViewHelpers.SecondaryButton("新規に戻す");
    private int? _editingUserId;

    public UserFormView(SqliteDatabase database, AppUser currentUser, Action backToDashboard)
    {
        _database = database;
        _currentUser = currentUser;
        _backToDashboard = backToDashboard;
        Content = Build();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _newButton.Click += (_, _) => ClearForm();
        _showPassword.IsCheckedChanged += (_, _) => UpdatePasswordVisibility();
        _ = LoadUsersAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.HorizontalAlignment = HorizontalAlignment.Left;
        backButton.Click += (_, _) => _backToDashboard();

        var form = ViewHelpers.Panel(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("ユーザー管理"),
                ViewHelpers.Body("ログインID、表示名、ロール、有効状態、パスワードを管理します。"),
                ViewHelpers.Label("ログインID"),
                _loginId,
                ViewHelpers.Label("表示名"),
                _displayName,
                ViewHelpers.Label("現在のパスワード"),
                _currentPassword,
                ViewHelpers.Label("新しいパスワード"),
                _password,
                ViewHelpers.Label("新しいパスワード(確認)"),
                _passwordConfirm,
                _showPassword,
                ViewHelpers.Label("ロール"),
                _role,
                new Border { Height = 8 },
                _isActive,
                new Border { Height = 8 },
                _saveButton,
                _newButton,
                _message
            }
        });

        var content = new Grid
        {
            Margin = new Thickness(28),
            ColumnDefinitions = new ColumnDefinitions("430,24,*"),
            RowDefinitions = new RowDefinitions("Auto,18,*"),
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
                        ViewHelpers.Heading(_currentUser.CompanyName),
                        ViewHelpers.Body("ユーザーマスタ")
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
            Content = _users
        };
        Grid.SetRow(listScroll, 2);

        var listGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,12,*"),
            Children =
            {
                ViewHelpers.Heading("登録済みユーザー", 20),
                listScroll
            }
        };

        var panel = ViewHelpers.Panel(listGrid);
        Grid.SetRow(panel, 2);
        Grid.SetColumn(panel, 2);
        return panel;
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await _database.GetUsersAsync(_currentUser.CompanyId);
            _users.Children.Clear();

            if (users.Count == 0)
            {
                _users.Children.Add(ViewHelpers.Body("まだ登録されていません。"));
                return;
            }

            foreach (var user in users)
            {
                _users.Children.Add(UserRow(user));
            }
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
    }

    private Control UserRow(UserAccount user)
    {
        var editButton = ViewHelpers.SecondaryButton("編集");
        editButton.Width = 80;
        editButton.Click += (_, _) => LoadForEdit(user);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,*,110,100,80"),
            Children =
            {
                Cell(user.LoginId, 0, FontWeight.SemiBold),
                Cell(user.DisplayName, 1),
                Cell(user.Role, 2),
                Cell(user.IsActive ? "有効" : "無効", 3),
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
            Child = grid
        };
    }

    private void LoadForEdit(UserAccount user)
    {
        _editingUserId = user.UserId;
        _loginId.Text = user.LoginId;
        _loginId.IsEnabled = false;
        _displayName.Text = user.DisplayName;
        _currentPassword.Text = "";
        _password.Text = "";
        _passwordConfirm.Text = "";
        _showPassword.IsChecked = false;
        _role.SelectedItem = user.Role;
        _isActive.IsChecked = user.IsActive;
        _saveButton.Content = "更新する";
        _currentPassword.IsVisible = ShouldRequireCurrentPassword(user.UserId);
        SetMessage($"{user.LoginId} を編集中です。パスワードを変更する場合のみ入力してください。", false);
    }

    private void ClearForm()
    {
        _editingUserId = null;
        _loginId.Text = "";
        _loginId.IsEnabled = true;
        _displayName.Text = "";
        _currentPassword.Text = "";
        _password.Text = "";
        _passwordConfirm.Text = "";
        _showPassword.IsChecked = false;
        _role.SelectedIndex = 1;
        _isActive.IsChecked = true;
        _saveButton.Content = "登録する";
        _currentPassword.IsVisible = false;
        SetMessage("新しいユーザーを登録できます。", false);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_loginId.Text) || string.IsNullOrWhiteSpace(_displayName.Text))
        {
            SetMessage("ログインIDと表示名を入力してください。", true);
            return;
        }

        if (_role.SelectedItem is not string selectedRole)
        {
            SetMessage("ロールを選択してください。", true);
            return;
        }

        var currentPassword = _currentPassword.Text ?? "";
        var password = _password.Text ?? "";
        var passwordConfirm = _passwordConfirm.Text ?? "";
        if (!_editingUserId.HasValue && string.IsNullOrWhiteSpace(password))
        {
            SetMessage("新規ユーザーのパスワードを入力してください。", true);
            return;
        }

        if (!_editingUserId.HasValue && string.IsNullOrWhiteSpace(passwordConfirm))
        {
            SetMessage("確認用パスワードを入力してください。", true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(password) || !string.IsNullOrWhiteSpace(passwordConfirm))
        {
            if (_editingUserId.HasValue && ShouldRequireCurrentPassword(_editingUserId.Value) && string.IsNullOrWhiteSpace(currentPassword))
            {
                SetMessage("パスワード変更時は現在のパスワードを入力してください。", true);
                return;
            }

            if (!string.Equals(password, passwordConfirm, StringComparison.Ordinal))
            {
                SetMessage("新しいパスワードと確認用パスワードが一致しません。", true);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(password) && password.Length < 8)
        {
            SetMessage("パスワードは8文字以上にしてください。", true);
            return;
        }

        try
        {
            _saveButton.IsEnabled = false;
            if (_editingUserId.HasValue)
            {
                await _database.UpdateUserAsync(
                    _currentUser.CompanyId,
                    _currentUser.UserId,
                    IsAdminSession(),
                    _editingUserId.Value,
                    _displayName.Text.Trim(),
                    selectedRole,
                    _isActive.IsChecked == true,
                    string.IsNullOrWhiteSpace(password) ? null : password,
                    string.IsNullOrWhiteSpace(currentPassword) ? null : currentPassword);
                SetMessage("ユーザーを更新しました。", false);
            }
            else
            {
                await _database.CreateUserAsync(
                    _currentUser.CompanyId,
                    _loginId.Text.Trim(),
                    _displayName.Text.Trim(),
                    password,
                    selectedRole,
                    _isActive.IsChecked == true);
                SetMessage("ユーザーを登録しました。", false);
                ClearForm();
            }

            await LoadUsersAsync();
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            SetMessage("同じログインIDが既に登録されています。", true);
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

    private void UpdatePasswordVisibility()
    {
        var show = _showPassword.IsChecked == true;
        _currentPassword.PasswordChar = show ? '\0' : '*';
        _password.PasswordChar = show ? '\0' : '*';
        _passwordConfirm.PasswordChar = show ? '\0' : '*';
    }

    private bool IsAdminSession()
    {
        return string.Equals(_currentUser.Role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRequireCurrentPassword(int editingUserId)
    {
        return !IsAdminSession() || editingUserId == _currentUser.UserId;
    }
}
