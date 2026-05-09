using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia.Controls;

namespace AccountingApp.Views;

public sealed class MainWindow : Window
{
    private readonly SqliteDatabase _database;
    private readonly bool _openedFromNewDatabase;
    private AppUser? _currentUser;
    private DateTime? _journalBookTargetMonth;
    private (int AccountId, int? SubAccountId, DateTime PeriodStart)? _generalLedgerSelection;
    private bool _allowWindowClose;
    private bool _isCloseConfirmationOpen;

    public MainWindow(SqliteDatabase database, bool openedFromNewDatabase)
    {
        _database = database;
        _openedFromNewDatabase = openedFromNewDatabase;
        Title = "会計ソフト";
        Width = 1500;
        Height = 820;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Closing += OnClosing;
        ShowLogin();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isCloseConfirmationOpen)
        {
            return;
        }

        _isCloseConfirmationOpen = true;
        try
        {
            var confirmed = await ShowCloseConfirmationAsync();
            if (!confirmed)
            {
                return;
            }

            _allowWindowClose = true;
            Close();
        }
        finally
        {
            _isCloseConfirmationOpen = false;
        }
    }

    private async Task<bool> ShowCloseConfirmationAsync()
    {
        var closeButton = ViewHelpers.PrimaryButton("終了する");
        closeButton.Width = 120;
        closeButton.Background = Avalonia.Media.Brush.Parse("#B42318");

        var cancelButton = ViewHelpers.SecondaryButton("キャンセル");
        cancelButton.Width = 120;

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { closeButton, cancelButton }
        };

        var dialog = new Window
        {
            Title = "終了確認",
            Width = 420,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Avalonia.Thickness(20),
                Child = ViewHelpers.Panel(new StackPanel
                {
                    Spacing = 18,
                    Children =
                    {
                        ViewHelpers.Heading("アプリを終了しますか", 22),
                        ViewHelpers.Body("未保存の内容がある場合は、終了前に保存してください。"),
                        buttons
                    }
                })
            }
        };

        closeButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        return await dialog.ShowDialog<bool>(this);
    }

    private void ShowLogin()
    {
        _currentUser = null;
        SetContent(new LoginView(_database, ShowDashboard, _openedFromNewDatabase));
    }

    private void ShowDashboard(AppUser user)
    {
        _currentUser = user;
        SetContent(new DashboardView(_database, user, ShowLogin, () => ShowSubAccountForm(), ShowAccountForm, ShowBusinessPartnerForm, ShowUserForm, ShowJournalForm, ShowJournalBook, ShowCashbook, ShowGeneralLedger, ShowBusinessPartnerTransactions, ShowTrialBalance, ShowBalanceSheet, ShowProfitAndLoss, ShowTaxSummary, ShowCompanySettings, ShowOperationLogs));
    }

    private void ShowSubAccountForm(int? accountId = null, bool returnToAccountForm = false)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Action? backToAccountForm = returnToAccountForm ? ShowAccountForm : null;
        SetContent(new SubAccountFormView(_database, _currentUser, () => ShowDashboard(_currentUser), backToAccountForm, accountId));
    }

    private void ShowAccountForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new AccountFormView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowSubAccountForm));
    }

    private void ShowUserForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new UserFormView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowBusinessPartnerForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new BusinessPartnerFormView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowJournalForm()
    {
        ShowJournalForm(null);
    }

    private void ShowJournalForm(string? entryNumber)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new JournalEntryFormView(_database, _currentUser, () => ShowDashboard(_currentUser), entryNumber));
    }

    private void ShowJournalFormFromJournalBook(string? entryNumber, DateTime targetMonth)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        _journalBookTargetMonth = new DateTime(targetMonth.Year, targetMonth.Month, 1);
        SetContent(new JournalEntryFormView(_database, _currentUser, () => ShowJournalBook(_journalBookTargetMonth), entryNumber));
    }

    private void ShowJournalFormFromGeneralLedger(string? entryNumber, int accountId, int? subAccountId, DateTime periodStart)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        _generalLedgerSelection = (accountId, subAccountId, periodStart.Date);
        SetContent(new JournalEntryFormView(_database, _currentUser, () => ShowGeneralLedger(_generalLedgerSelection), entryNumber));
    }

    private void ShowCashbook()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new CashbookView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalForm));
    }

    private void ShowGeneralLedger()
    {
        ShowGeneralLedger(null);
    }

    private void ShowGeneralLedger((int AccountId, int? SubAccountId, DateTime PeriodStart)? selection)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        _generalLedgerSelection = selection;
        SetContent(new GeneralLedgerView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalFormFromGeneralLedger, _generalLedgerSelection));
    }

    private void ShowBusinessPartnerTransactions()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new BusinessPartnerTransactionsView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalForm));
    }

    private void ShowTrialBalance()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new TrialBalanceView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowBalanceSheet()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new BalanceSheetView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowProfitAndLoss()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new ProfitAndLossView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowTaxSummary()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new TaxSummaryView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowCompanySettings()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new CompanySettingsView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowDashboard, ShowLogin));
    }

    private void ShowOperationLogs()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new OperationLogView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowJournalBook()
    {
        ShowJournalBook(null);
    }

    private void ShowJournalBook(DateTime? targetMonth)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        _journalBookTargetMonth = targetMonth.HasValue
            ? new DateTime(targetMonth.Value.Year, targetMonth.Value.Month, 1)
            : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        SetContent(new JournalBookView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalFormFromJournalBook, _journalBookTargetMonth.Value));
    }

    private void SetContent(Control view)
    {
        if (view is AccountFormView)
        {
            Content = view;
            return;
        }

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = view
        };
    }
}
