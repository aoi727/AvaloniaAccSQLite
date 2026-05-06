using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class DashboardView : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _signOut;
    private readonly Action _openSubAccountForm;
    private readonly Action _openAccountForm;
    private readonly Action _openBusinessPartnerForm;
    private readonly Action _openUserForm;
    private readonly Action _openJournalForm;
    private readonly Action _openJournalBook;
    private readonly Action _openCashbook;
    private readonly Action _openGeneralLedger;
    private readonly Action _openBusinessPartnerTransactions;
    private readonly Action _openTrialBalance;
    private readonly Action _openBalanceSheet;
    private readonly Action _openProfitAndLoss;
    private readonly Action _openTaxSummary;
    private readonly Action _openCompanySettings;
    private readonly Action _openOperationLogs;
    private readonly WrapPanel _summary = new()
    {
        Orientation = Orientation.Horizontal,
        ItemWidth = 190,
        ItemHeight = 84
    };
    private readonly TextBlock _message = ViewHelpers.Body("ホーム画面を読み込み中です。");

    public DashboardView(
        SqliteDatabase database,
        AppUser user,
        Action signOut,
        Action openSubAccountForm,
        Action openAccountForm,
        Action openBusinessPartnerForm,
        Action openUserForm,
        Action openJournalForm,
        Action openJournalBook,
        Action openCashbook,
        Action openGeneralLedger,
        Action openBusinessPartnerTransactions,
        Action openTrialBalance,
        Action openBalanceSheet,
        Action openProfitAndLoss,
        Action openTaxSummary,
        Action openCompanySettings,
        Action openOperationLogs)
    {
        _database = database;
        _user = user;
        _signOut = signOut;
        _openSubAccountForm = openSubAccountForm;
        _openAccountForm = openAccountForm;
        _openBusinessPartnerForm = openBusinessPartnerForm;
        _openUserForm = openUserForm;
        _openJournalForm = openJournalForm;
        _openJournalBook = openJournalBook;
        _openCashbook = openCashbook;
        _openGeneralLedger = openGeneralLedger;
        _openBusinessPartnerTransactions = openBusinessPartnerTransactions;
        _openTrialBalance = openTrialBalance;
        _openBalanceSheet = openBalanceSheet;
        _openProfitAndLoss = openProfitAndLoss;
        _openTaxSummary = openTaxSummary;
        _openCompanySettings = openCompanySettings;
        _openOperationLogs = openOperationLogs;
        Content = Build();
        _ = LoadAsync();
    }

    private Control Build()
    {
        var signOutButton = ViewHelpers.SecondaryButton("ログアウト");
        signOutButton.Width = 110;
        signOutButton.Click += (_, _) => _signOut();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        ViewHelpers.Heading(_user.CompanyName, 24),
                        ViewHelpers.Body($"{_user.DisplayName} / {_user.Role}")
                    }
                },
                signOutButton
            }
        };
        Grid.SetColumn(signOutButton, 1);

        var summaryPanel = CreatePanel(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                ViewHelpers.Heading("サマリー", 18),
                _summary
            }
        });

        var workPanel = CreateSectionPanel(
            "日常業務",
            [
                CreateMenuButton("仕訳入力", true, _ => _openJournalForm()),
                CreateMenuButton("仕訳帳", false, _ => _openJournalBook())
            ]);

        var ledgerPanel = CreateSectionPanel(
            "帳票",
            [
                CreateMenuButton("出納帳", false, _ => _openCashbook()),
                CreateMenuButton("総勘定元帳", false, _ => _openGeneralLedger()),
                CreateMenuButton("取引先別取引一覧", false, _ => _openBusinessPartnerTransactions()),
                CreateMenuButton("試算表", false, _ => _openTrialBalance()),
                CreateMenuButton("貸借対照表", false, _ => _openBalanceSheet()),
                CreateMenuButton("損益計算書", false, _ => _openProfitAndLoss()),
                CreateMenuButton("消費税集計", false, _ => _openTaxSummary())
            ]);

        var managementPanel = string.Equals(_user.Role, "admin", StringComparison.OrdinalIgnoreCase)
            ? CreateSectionPanel(
                "管理",
                [
                    CreateMenuButton("会社設定", false, _ => _openCompanySettings()),
                    CreateMenuButton("勘定科目", false, _ => _openAccountForm()),
                    CreateMenuButton("補助科目", false, _ => _openSubAccountForm()),
                    CreateMenuButton("取引先", false, _ => _openBusinessPartnerForm()),
                    CreateMenuButton("ユーザー", false, _ => _openUserForm()),
                    CreateMenuButton("操作履歴", false, _ => _openOperationLogs())
                ])
            : CreateSectionPanel(
                "管理",
                [
                    ViewHelpers.Body("このユーザーでは管理機能を利用できません。")
                ]);

        var sectionGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 14,
            RowDefinitions = new RowDefinitions("Auto"),
            Children =
            {
                workPanel,
                ledgerPanel,
                managementPanel
            }
        };
        Grid.SetColumn(ledgerPanel, 1);
        Grid.SetColumn(managementPanel, 2);

        var messageBar = new Border
        {
            Background = Brush.Parse("#EEF4FB"),
            BorderBrush = Brush.Parse("#D8E4F2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8),
            Child = _message
        };

        var footerBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                messageBar
            }
        };

        if (string.Equals(_user.Role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            var clearAllButton = CreateDangerButton("ALL CLEAR", button => ConfirmAndClearAllAsync(button));
            clearAllButton.Width = 120;
            clearAllButton.HorizontalAlignment = HorizontalAlignment.Right;
            clearAllButton.VerticalAlignment = VerticalAlignment.Bottom;
            clearAllButton.Opacity = 0.76;
            Grid.SetColumn(clearAllButton, 1);
            footerBar.Children.Add(clearAllButton);
        }

        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                header,
                summaryPanel,
                sectionGrid,
                footerBar
            }
        };

        return new Border
        {
            Background = Brush.Parse("#F3F6FB"),
            Padding = new Thickness(18),
            Child = body
        };
    }

    private static Border CreatePanel(Control child)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = child
        };
    }

    private Border CreateSectionPanel(string title, IEnumerable<Control> content)
    {
        var stack = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                ViewHelpers.Heading(title, 18)
            }
        };

        foreach (var control in content)
        {
            stack.Children.Add(control);
        }

        return CreatePanel(stack);
    }

    private static Button CreateMenuButton(string title, bool isPrimary, Action<Button> onClick)
    {
        var button = isPrimary ? ViewHelpers.PrimaryButton(title) : ViewHelpers.SecondaryButton(title);
        button.Height = 40;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Click += (_, _) => onClick(button);
        return button;
    }

    private static Button CreateDangerButton(string title, Func<Button, Task> onClick)
    {
        var button = ViewHelpers.SecondaryButton(title);
        button.Height = 32;
        button.Background = Brush.Parse("#B42318");
        button.Foreground = Brushes.White;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Click += async (_, _) => await onClick(button);
        return button;
    }

    private async Task LoadAsync()
    {
        try
        {
            var summary = await _database.GetDashboardSummaryAsync(_user.CompanyId);

            _summary.Children.Clear();
            _summary.Children.Add(SummaryCard("勘定科目", summary.AccountCount.ToString("N0")));
            _summary.Children.Add(SummaryCard("補助科目", summary.SubAccountCount.ToString("N0")));
            _summary.Children.Add(SummaryCard("仕訳件数", summary.EntryCount.ToString("N0")));
            _summary.Children.Add(SummaryCard("仕訳金額", summary.TotalEntryAmount.ToString("N0")));

            _message.Text = "ホーム画面を表示しました。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private static Control SummaryCard(string label, string value)
    {
        return new Border
        {
            Width = 190,
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D9DEE7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        FontSize = 12,
                        Foreground = Brush.Parse("#607086")
                    },
                    new TextBlock
                    {
                        Text = value,
                        FontSize = 21,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brush.Parse("#172033"),
                        TextWrapping = TextWrapping.NoWrap
                    }
                }
            }
        };
    }

    private async Task ConfirmAndClearAllAsync(Button clearAllButton)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            _message.Text = "確認ダイアログを表示できませんでした。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        var confirmation = new TextBox
        {
            Width = 220,
            PlaceholderText = "CLEAR と入力"
        };

        var warning = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                ViewHelpers.Heading("ALL CLEAR を実行します", 22),
                ViewHelpers.Body("ユーザー、勘定科目、補助科目、取引先、仕訳をすべて削除します。"),
                ViewHelpers.Body("実行後はログイン画面に戻ります。実行する場合は CLEAR と入力してください。"),
                confirmation
            }
        };

        var executeButton = ViewHelpers.PrimaryButton("実行する");
        executeButton.Width = 120;
        var cancelButton = ViewHelpers.SecondaryButton("キャンセル");
        cancelButton.Width = 120;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { executeButton, cancelButton }
        };

        var dialog = new Window
        {
            Title = "ALL CLEAR 確認",
            Width = 520,
            Height = 260,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = CreatePanel(new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 18,
                Children = { warning, buttons }
            })
        };

        executeButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
        {
            return;
        }

        if (!string.Equals(confirmation.Text?.Trim(), "CLEAR", StringComparison.Ordinal))
        {
            _message.Text = "確認キーワードが一致しなかったため、中止しました。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        try
        {
            clearAllButton.IsEnabled = false;
            await _database.ClearAllDataAsync();
            _signOut();
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            clearAllButton.IsEnabled = true;
        }
    }
}
