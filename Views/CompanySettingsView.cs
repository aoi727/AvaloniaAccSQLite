using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AccountingApp.Views;

public sealed class CompanySettingsView : UserControl
{
    private sealed record ClosingDayOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TaxEntryMethodOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly Action<AppUser> _switchCompany;
    private readonly TextBox _companyName = new();
    private readonly DatePicker _fiscalYearStart = new();
    private readonly ComboBox _closingDay = new() { ItemsSource = CreateClosingDayOptions(), SelectedIndex = 30 };
    private readonly ComboBox _taxEntryMethod = new() { ItemsSource = CreateTaxEntryMethodOptions(), SelectedIndex = 0 };
    private readonly CheckBox _isTaxExempt = new() { Content = "免税事業者" };
    private readonly TextBlock _carryForwardPeriod = ViewHelpers.Body("");
    private readonly TextBlock _carryForwardAccount = ViewHelpers.Body("");
    private readonly TextBlock _carryForwardAmount = ViewHelpers.Body("");
    private readonly TextBlock _carryForwardStatus = ViewHelpers.Body("");
    private readonly TextBox _unlockReason = new() { PlaceholderText = "修正理由を入力してください" };
    private readonly DatePicker _monthlyLockDate = new();
    private readonly TextBlock _monthlyLockPeriod = ViewHelpers.Body("");
    private readonly TextBlock _monthlyLockStatus = ViewHelpers.Body("");
    private readonly TextBox _monthlyUnlockReason = new() { PlaceholderText = "月次ロック解除の理由を入力してください" };
    private readonly TextBlock _databasePath = ViewHelpers.Body("");
    private readonly TextBlock _message = ViewHelpers.Body("会社設定を読み込み中です。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("会社設定を保存");
    private readonly Button _carryForwardButton = ViewHelpers.SecondaryButton("年度締めを実行");
    private readonly Button _unlockClosingButton = ViewHelpers.SecondaryButton("年度締めを解除");
    private readonly Button _monthlyLockButton = ViewHelpers.SecondaryButton("月次をロック");
    private readonly Button _monthlyUnlockButton = ViewHelpers.SecondaryButton("月次ロックを解除");
    private readonly Button _backupButton = ViewHelpers.SecondaryButton("DBバックアップを保存");
    private bool _isLoadingMonthlyLockStatus;

    public CompanySettingsView(SqliteDatabase database, AppUser user, Action backToDashboard, Action<AppUser> switchCompany)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _switchCompany = switchCompany;
        Content = Build();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _carryForwardButton.Click += async (_, _) => await ExecuteCarryForwardAsync();
        _unlockClosingButton.Click += async (_, _) => await UnlockClosingAsync();
        _monthlyLockButton.Click += async (_, _) => await LockMonthlyPeriodAsync();
        _monthlyUnlockButton.Click += async (_, _) => await UnlockMonthlyPeriodAsync();
        _backupButton.Click += async (_, _) => await BackupDatabaseAsync();
        _monthlyLockDate.SelectedDateChanged += async (_, _) => await LoadMonthlyLockStatusAsync();
        _isTaxExempt.IsCheckedChanged += (_, _) => ApplyTaxExemptState(_taxEntryMethod, _isTaxExempt);
        _ = LoadAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.Click += (_, _) => _backToDashboard();

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
                        ViewHelpers.Body("会社設定")
                    }
                },
                backButton
            }
        };
        Grid.SetColumn(backButton, 1);

        var settingsPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("現在の会社設定", 20),
                ViewHelpers.Body("会社名、年度開始日、締め日、消費税の入力方法を更新できます。"),
                ViewHelpers.Label("会社名"),
                _companyName,
                ViewHelpers.Label("年度開始日"),
                _fiscalYearStart,
                ViewHelpers.Label("締め日"),
                _closingDay,
                _isTaxExempt,
                ViewHelpers.Label("消費税入力方式"),
                _taxEntryMethod,
                ViewHelpers.Body("免税事業者の場合は総額方式に固定されます。"),
                new Border { Height = 8 },
                _saveButton
            }
        });

        _carryForwardButton.Width = 180;
        _unlockClosingButton.Width = 180;
        _monthlyLockButton.Width = 180;
        _monthlyUnlockButton.Width = 180;
        _backupButton.Width = 220;

        var carryForwardPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 6,
            Children =
            {
                ViewHelpers.Heading("年度締め", 20),
                ViewHelpers.Body("対象年度を締めて、翌年度開始日に繰越仕訳を作成します。締め済み年度は仕訳の登録や削除がロックされます。"),
                ViewHelpers.Label("対象期間"),
                _carryForwardPeriod,
                ViewHelpers.Label("繰越損益の振替先"),
                _carryForwardAccount,
                ViewHelpers.Label("当期純利益"),
                _carryForwardAmount,
                ViewHelpers.Label("締めの状態"),
                _carryForwardStatus,
                ViewHelpers.Label("締め解除理由"),
                _unlockReason,
                new Border { Height = 8 },
                _carryForwardButton,
                _unlockClosingButton
            }
        });

        var monthlyLockPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 6,
            Children =
            {
                ViewHelpers.Heading("月次ロック", 20),
                ViewHelpers.Body("選択した日付が属する会計月をロックします。ロック済み月次は仕訳の登録、更新、削除、CSV取込ができなくなります。"),
                ViewHelpers.Label("対象日"),
                _monthlyLockDate,
                ViewHelpers.Label("対象期間"),
                _monthlyLockPeriod,
                ViewHelpers.Label("ロック状態"),
                _monthlyLockStatus,
                ViewHelpers.Label("解除理由"),
                _monthlyUnlockReason,
                new Border { Height = 8 },
                _monthlyLockButton,
                _monthlyUnlockButton
            }
        });

        var backupPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 6,
            Children =
            {
                ViewHelpers.Heading("DBバックアップ", 20),
                ViewHelpers.Body("現在利用中の SQLite DB を別ファイルとして保存します。障害時の復旧や定期退避に使えます。"),
                ViewHelpers.Label("現在のDBファイル"),
                _databasePath,
                new Border { Height = 8 },
                _backupButton
            }
        });

        var body = new StackPanel
        {
            Spacing = 18,
            Children = { header, settingsPanel, carryForwardPanel, monthlyLockPanel, backupPanel, _message }
        };

        return new ScrollViewer
        {
            Content = new Grid
            {
                Margin = new Thickness(28),
                Children = { body }
            }
        };
    }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            _companyName.Text = settings.CompanyName;
            _fiscalYearStart.SelectedDate = new DateTimeOffset(settings.FiscalYearStart.Date);
            _closingDay.SelectedItem = CreateClosingDayOptions().FirstOrDefault(x => x.Value == settings.ClosingDay);
            _taxEntryMethod.SelectedItem = CreateTaxEntryMethodOptions().FirstOrDefault(x => x.Value == settings.TaxEntryMethod);
            _isTaxExempt.IsChecked = settings.IsTaxExempt;
            ApplyTaxExemptState(_taxEntryMethod, _isTaxExempt);
            _databasePath.Text = _database.GetDatabaseFilePath() ?? "現在のDBパスを取得できません。";
            _monthlyLockDate.SelectedDate ??= new DateTimeOffset(DateTime.Today);

            await LoadCarryForwardStatusAsync();
            await LoadMonthlyLockStatusAsync();
            _message.Text = "会社設定を表示しました。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private async Task SaveAsync()
    {
        if (_closingDay.SelectedItem is not ClosingDayOption closingDay)
        {
            SetError("締め日を選択してください。");
            return;
        }

        if (_taxEntryMethod.SelectedItem is not TaxEntryMethodOption taxEntryMethod)
        {
            SetError("消費税入力方式を選択してください。");
            return;
        }

        try
        {
            _saveButton.IsEnabled = false;
            var fiscalYearStart = _fiscalYearStart.SelectedDate?.DateTime.Date ?? DateTime.Today;
            var isTaxExempt = _isTaxExempt.IsChecked == true;
            await _database.UpdateCompanySettingsAsync(
                _user.CompanyId,
                _companyName.Text ?? "",
                fiscalYearStart,
                closingDay.Value,
                taxEntryMethod.Value,
                isTaxExempt);

            var updatedUser = _user with { CompanyName = (_companyName.Text ?? "").Trim() };
            _message.Text = "会社設定を更新しました。";
            _message.Foreground = Brush.Parse("#1E6B52");
            _switchCompany(updatedUser);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _saveButton.IsEnabled = true;
        }
    }

    private async Task BackupDatabaseAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            SetError("バックアップ保存ダイアログを開けませんでした。");
            return;
        }

        var currentDatabasePath = _database.GetDatabaseFilePath();
        var baseFileName = string.IsNullOrWhiteSpace(currentDatabasePath)
            ? "accounting_app"
            : Path.GetFileNameWithoutExtension(currentDatabasePath);

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "DBバックアップを保存",
            SuggestedFileName = $"{baseFileName}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
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
            return;
        }

        try
        {
            _backupButton.IsEnabled = false;
            await _database.BackupDatabaseAsync(path);
            _message.Text = $"DBバックアップを保存しました: {file!.Name}";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _backupButton.IsEnabled = true;
        }
    }

    private async Task LoadCarryForwardStatusAsync()
    {
        var status = await _database.GetAnnualCarryForwardStatusAsync(_user.CompanyId, DateTime.Today);
        _carryForwardPeriod.Text = $"{status.SourceFiscalYearStart:yyyy/MM/dd} から {status.SourceFiscalYearEnd:yyyy/MM/dd} を締めて {status.NextFiscalYearStart:yyyy/MM/dd} に繰越";
        _carryForwardAccount.Text = status.EquityAccountDisplayName;
        _carryForwardAmount.Text = FormatFinancialStatementAmount(status.NetIncome);
        _unlockReason.Text = "";

        if (status.IsClosed)
        {
            _carryForwardStatus.Text = status.ExecutedAt.HasValue
                ? $"締め済み: {status.EntryNumber} ({status.ExecutedAt:yyyy/MM/dd HH:mm})"
                : $"締め済み: {status.EntryNumber}";
            _carryForwardStatus.Foreground = Brush.Parse("#1E6B52");
            _carryForwardButton.IsEnabled = false;
            _unlockReason.IsEnabled = true;
            _unlockClosingButton.IsEnabled = true;
            return;
        }

        _carryForwardStatus.Text = status.AlreadyExecuted
            ? $"解除中: 再締めで繰越仕訳を更新します ({status.EntryNumber})"
            : "未締め";
        _carryForwardStatus.Foreground = Brush.Parse("#4A5568");
        _carryForwardButton.IsEnabled = true;
        _unlockReason.IsEnabled = false;
        _unlockClosingButton.IsEnabled = false;
    }

    private async Task ExecuteCarryForwardAsync()
    {
        try
        {
            _carryForwardButton.IsEnabled = false;
            await _database.CloseFiscalYearAsync(_user.CompanyId, _user.UserId, DateTime.Today);
            await LoadCarryForwardStatusAsync();
            _message.Text = "年度締めを実行しました。";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            await LoadCarryForwardStatusAsync();
        }
    }

    private async Task UnlockClosingAsync()
    {
        try
        {
            var status = await _database.GetAnnualCarryForwardStatusAsync(_user.CompanyId, DateTime.Today);
            _unlockClosingButton.IsEnabled = false;
            await _database.UnlockAnnualClosingAsync(_user.CompanyId, _user.UserId, status.SourceFiscalYearStart, _unlockReason.Text ?? "");
            await LoadCarryForwardStatusAsync();
            _message.Text = "年度締めを解除しました。必要に応じて再度年度締めを実行してください。";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            await LoadCarryForwardStatusAsync();
        }
    }

    private async Task LoadMonthlyLockStatusAsync()
    {
        if (_isLoadingMonthlyLockStatus)
        {
            return;
        }

        try
        {
            _isLoadingMonthlyLockStatus = true;
            var targetDate = (_monthlyLockDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
            if (!_monthlyLockDate.SelectedDate.HasValue)
            {
                _monthlyLockDate.SelectedDate = new DateTimeOffset(targetDate);
            }

            var status = await _database.GetMonthlyLockStatusAsync(_user.CompanyId, targetDate);
            _monthlyLockPeriod.Text = $"{status.PeriodStart:yyyy/MM/dd} から {status.PeriodEnd:yyyy/MM/dd}";
            _monthlyUnlockReason.Text = "";

            if (status.IsLocked)
            {
                _monthlyLockStatus.Text = status.LockedAt.HasValue
                    ? $"ロック済み ({status.LockedAt:yyyy/MM/dd HH:mm})"
                    : "ロック済み";
                _monthlyLockStatus.Foreground = Brush.Parse("#1E6B52");
                _monthlyLockButton.IsEnabled = false;
                _monthlyUnlockReason.IsEnabled = true;
                _monthlyUnlockButton.IsEnabled = true;
                return;
            }

            _monthlyLockStatus.Text = status.UnlockedAt.HasValue
                ? $"解除中 ({status.UnlockedAt:yyyy/MM/dd HH:mm})"
                : "未ロック";
            _monthlyLockStatus.Foreground = Brush.Parse("#4A5568");
            _monthlyLockButton.IsEnabled = true;
            _monthlyUnlockReason.IsEnabled = false;
            _monthlyUnlockButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _isLoadingMonthlyLockStatus = false;
        }
    }

    private async Task LockMonthlyPeriodAsync()
    {
        try
        {
            var targetDate = (_monthlyLockDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
            _monthlyLockButton.IsEnabled = false;
            await _database.LockMonthlyPeriodAsync(_user.CompanyId, _user.UserId, targetDate);
            await LoadMonthlyLockStatusAsync();
            _message.Text = "月次をロックしました。対象期間の仕訳変更はできません。";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            await LoadMonthlyLockStatusAsync();
        }
    }

    private async Task UnlockMonthlyPeriodAsync()
    {
        try
        {
            var targetDate = (_monthlyLockDate.SelectedDate?.DateTime ?? DateTime.Today).Date;
            _monthlyUnlockButton.IsEnabled = false;
            await _database.UnlockMonthlyPeriodAsync(_user.CompanyId, _user.UserId, targetDate, _monthlyUnlockReason.Text ?? "");
            await LoadMonthlyLockStatusAsync();
            _message.Text = "月次ロックを解除しました。必要な修正後は再度ロックしてください。";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            await LoadMonthlyLockStatusAsync();
        }
    }

    private void SetError(string message)
    {
        _message.Text = message;
        _message.Foreground = Brush.Parse("#B42318");
    }

    private static void ApplyTaxExemptState(ComboBox taxEntryMethod, CheckBox isTaxExempt)
    {
        var exempt = isTaxExempt.IsChecked == true;
        taxEntryMethod.IsEnabled = !exempt;
        if (exempt)
        {
            taxEntryMethod.SelectedItem = CreateTaxEntryMethodOptions().First(x => x.Value == "gross");
        }
    }

    private static IReadOnlyList<ClosingDayOption> CreateClosingDayOptions()
    {
        var options = Enumerable.Range(1, 30)
            .Select(day => new ClosingDayOption(day, $"{day}日"))
            .ToList();
        options.Add(new ClosingDayOption(31, "末日"));
        return options;
    }

    private static IReadOnlyList<TaxEntryMethodOption> CreateTaxEntryMethodOptions()
    {
        return
        [
            new("gross", "総額方式"),
            new("net", "税抜方式")
        ];
    }

    private static string FormatFinancialStatementAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount):N0}"
            : amount.ToString("N0");
    }
}
