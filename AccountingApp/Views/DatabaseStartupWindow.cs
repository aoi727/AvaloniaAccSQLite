using AccountingApp.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AccountingApp.Views;

public sealed class DatabaseStartupWindow : Window
{
    private readonly Action<SqliteDatabase, bool> _databaseSelected;
    private readonly TextBlock _message;
    private readonly Button _openButton;
    private readonly Button _newButton;
    private bool _started;

    public DatabaseStartupWindow(Action<SqliteDatabase, bool> databaseSelected)
    {
        _databaseSelected = databaseSelected;
        Title = "会計ソフト - DB選択";
        Width = 640;
        Height = 360;
        MinWidth = 560;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _message = ViewHelpers.Body("");
        _openButton = ViewHelpers.PrimaryButton("DBファイルを選ぶ");
        _newButton = ViewHelpers.SecondaryButton("新しいDBを作成");

        _openButton.Click += async (_, _) => await OpenExistingDatabaseAsync();
        _newButton.Click += async (_, _) => await CreateNewDatabaseAsync();

        Content = Build();
        Opened += async (_, _) => await StartAsync();
    }

    private Control Build()
    {
        return new Border
        {
            Padding = new Thickness(32),
            Background = Brush.Parse("#F5F7FA"),
            Child = ViewHelpers.Panel(new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    ViewHelpers.Heading("使用するDBファイルを選択"),
                    ViewHelpers.Body("前回指定したDBファイルが見つかれば自動で開きます。見つからない場合は、既存のSQLite DBを選ぶか、新しいDBファイルを作成してください。"),
                    _message,
                    new Border { Height = 8 },
                    _newButton,
                    _openButton
                }
            })
        };
    }

    private async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        if (AppSettings.TryGetExistingConfiguredDatabase(out var databasePath))
        {
            OpenDatabase(databasePath, openedFromNewDatabase: false);
            return;
        }

        var configuredPath = AppSettings.GetConfiguredDatabasePath();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            SetMessage($"前回のDBファイルが見つかりません: {configuredPath}{Environment.NewLine}新規作成するか、既存のDBファイルを選んでください。", true);
        }
        else
        {
            SetMessage("DBファイルがまだ設定されていません。新規作成するか、既存のDBファイルを選んでください。", false);
        }
    }

    private async Task OpenExistingDatabaseAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SQLite DBファイルを開く",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SQLite DB")
                {
                    Patterns = ["*.db", "*.sqlite", "*.sqlite3"],
                    MimeTypes = ["application/vnd.sqlite3", "application/x-sqlite3", "application/octet-stream"]
                },
                FilePickerFileTypes.All
            ]
        });

        var file = files.Count > 0 ? files[0] : null;
        var path = file?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            SetMessage("DBファイルの選択をキャンセルしました。新規作成するか、既存のDBファイルを選んでください。", false);
            return;
        }

        if (!File.Exists(path))
        {
            SetMessage("指定されたDBファイルが見つかりません。", true);
            return;
        }

        AppSettings.SaveDatabasePath(path);
        OpenDatabase(path, openedFromNewDatabase: false);
    }

    private async Task CreateNewDatabaseAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "新しいSQLite DBファイルを作成",
            SuggestedFileName = "accounting_app.db",
            DefaultExtension = "db",
            FileTypeChoices =
            [
                new FilePickerFileType("SQLite DB")
                {
                    Patterns = ["*.db", "*.sqlite", "*.sqlite3"],
                    MimeTypes = ["application/vnd.sqlite3", "application/x-sqlite3", "application/octet-stream"]
                }
            ],
            ShowOverwritePrompt = true
        });

        var path = file?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            SetMessage("新規作成をキャンセルしました。", false);
            return;
        }

        try
        {
            var database = new SqliteDatabase(AppSettings.BuildSqliteConnectionString(path));
            await database.InitializeSchemaAsync();
            AppSettings.SaveDatabasePath(path);
            OpenDatabase(path, openedFromNewDatabase: true);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
    }

    private void OpenDatabase(string databasePath, bool openedFromNewDatabase)
    {
        var database = new SqliteDatabase(AppSettings.BuildSqliteConnectionString(databasePath));
        _databaseSelected(database, openedFromNewDatabase);
        Close();
    }

    private void SetMessage(string message, bool isError)
    {
        _message.Text = message;
        _message.Foreground = isError ? Brush.Parse("#B42318") : Brush.Parse("#4A5568");
    }
}
