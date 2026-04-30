using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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
    private readonly TextBlock _message = ViewHelpers.Body("会社設定を読み込み中です。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("会社情報を保存");
    private readonly Button _carryForwardButton = ViewHelpers.SecondaryButton("年度締めを実行");
    private readonly Button _unlockClosingButton = ViewHelpers.SecondaryButton("年度締めを解除");

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
                ViewHelpers.Heading("現在の会社", 20),
                ViewHelpers.Body("会社名、会計年度開始日、締め日、消費税の設定を管理します。"),
                ViewHelpers.Label("会社名"),
                _companyName,
                ViewHelpers.Label("会計年度開始日"),
                _fiscalYearStart,
                ViewHelpers.Label("締め日"),
                _closingDay,
                _isTaxExempt,
                ViewHelpers.Label("消費税記帳方式"),
                _taxEntryMethod,
                ViewHelpers.Body("免税事業者では税込方式に固定されます。"),
                new Border { Height = 8 },
                _saveButton
            }
        });
        _carryForwardButton.Width = 180;
        _unlockClosingButton.Width = 180;

        var carryForwardPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 6,
            Children =
            {
                ViewHelpers.Heading("年度締め", 20),
                ViewHelpers.Body("対象年度を締め、翌年度開始日の繰越仕訳を作成または更新します。締め済み年度は仕訳の保存・削除がロックされます。"),
                ViewHelpers.Label("対象期間"),
                _carryForwardPeriod,
                ViewHelpers.Label("繰越先の資本科目"),
                _carryForwardAccount,
                ViewHelpers.Label("当期純利益"),
                _carryForwardAmount,
                ViewHelpers.Label("締め状態"),
                _carryForwardStatus,
                ViewHelpers.Label("締め解除理由"),
                _unlockReason,
                new Border { Height = 8 },
                _carryForwardButton,
                _unlockClosingButton
            }
        });

        var body = new StackPanel
        {
            Spacing = 18,
            Children = { header, settingsPanel, carryForwardPanel, _message }
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

            await LoadCarryForwardStatusAsync();
            _message.Text = "会社設定を表示しています。";
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
            SetError("消費税記帳方式を選択してください。");
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
            _message.Text = "会社情報を更新しました。";
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
            _message.Text = "年度締めを解除しました。修正後は再度年度締めを実行してください。";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            await LoadCarryForwardStatusAsync();
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
            new("gross", "税込方式"),
            new("net", "税抜方式")
        ];
    }

    private static string FormatFinancialStatementAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount):N0}"
            : amount.ToString("N0");
    }

    private static DateTime GetDefaultFiscalYearStart(DateTime referenceDate)
    {
        return new DateTime(referenceDate.Year, 1, 1);
    }
}
