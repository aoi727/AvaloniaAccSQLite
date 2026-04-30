using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Views;

public sealed class BusinessPartnerFormView : UserControl
{
    private static readonly PartnerChoice[] PartnerTypes =
    [
        new("supplier", "仕入先"),
        new("customer", "得意先"),
        new("both", "得意先・仕入先"),
        new("other", "その他")
    ];

    private static readonly PartnerChoice[] InvoiceStatuses =
    [
        new("qualified", "適格請求書発行事業者"),
        new("unregistered", "登録なし"),
        new("exempt", "免税事業者"),
        new("unknown", "未確認")
    ];

    private readonly SqliteDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly TextBox _code = new() { PlaceholderText = "例: SUP-001 / CUST-001" };
    private readonly TextBox _name = new() { PlaceholderText = "例: 株式会社サンプル" };
    private readonly ComboBox _partnerType = new() { ItemsSource = PartnerTypes, SelectedIndex = 0 };
    private readonly ComboBox _invoiceStatus = new() { ItemsSource = InvoiceStatuses, SelectedIndex = 0 };
    private readonly TextBox _registrationNumber = new() { PlaceholderText = "例: T1234567890123" };
    private readonly CheckBox _isActive = new() { Content = "有効", IsChecked = true };
    private readonly StackPanel _partners = new() { Spacing = 8 };
    private readonly TextBlock _message = ViewHelpers.Body("取引先を追加・編集できます。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("登録する");
    private readonly Button _newButton = ViewHelpers.SecondaryButton("新規入力");
    private int? _editingPartnerId;

    public BusinessPartnerFormView(SqliteDatabase database, AppUser user, Action backToDashboard)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        Content = Build();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _newButton.Click += (_, _) => ClearForm();
        _ = LoadPartnersAsync();
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
                ViewHelpers.Heading("取引先マスタ"),
                ViewHelpers.Body("インボイス対応に使う取引先名、登録番号、事業者区分を管理します。"),
                ViewHelpers.Label("取引先コード"),
                _code,
                ViewHelpers.Label("取引先名"),
                _name,
                ViewHelpers.Label("取引先区分"),
                _partnerType,
                ViewHelpers.Label("インボイス区分"),
                _invoiceStatus,
                ViewHelpers.Label("登録番号"),
                _registrationNumber,
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
                        ViewHelpers.Heading(_user.CompanyName),
                        ViewHelpers.Body("取引先マスタ")
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
            Content = _partners
        };
        Grid.SetRow(listScroll, 2);

        var listGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,12,*"),
            Children =
            {
                ViewHelpers.Heading("登録済み取引先", 20),
                listScroll
            }
        };

        var panel = ViewHelpers.Panel(listGrid);
        Grid.SetRow(panel, 2);
        Grid.SetColumn(panel, 2);
        return panel;
    }

    private async Task LoadPartnersAsync()
    {
        try
        {
            var partners = await _database.GetBusinessPartnersAsync(_user.CompanyId);
            _partners.Children.Clear();

            if (partners.Count == 0)
            {
                _partners.Children.Add(ViewHelpers.Body("まだ登録されていません。"));
                return;
            }

            foreach (var partner in partners)
            {
                _partners.Children.Add(PartnerRow(partner));
            }
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
    }

    private Control PartnerRow(BusinessPartner partner)
    {
        var editButton = ViewHelpers.SecondaryButton("編集");
        editButton.Width = 80;
        editButton.Click += (_, _) => LoadForEdit(partner);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*,130,180,150,80,80"),
            Children =
            {
                Cell(partner.Code, 0, FontWeight.SemiBold),
                Cell(partner.Name, 1),
                Cell(ToPartnerTypeLabel(partner.PartnerType), 2),
                Cell(ToInvoiceStatusLabel(partner.InvoiceStatus), 3),
                Cell(partner.RegistrationNumber ?? "", 4),
                Cell(partner.IsActive ? "有効" : "無効", 5),
                editButton
            }
        };
        Grid.SetColumn(editButton, 6);

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

    private void LoadForEdit(BusinessPartner partner)
    {
        _editingPartnerId = partner.PartnerId;
        _code.Text = partner.Code;
        _name.Text = partner.Name;
        _partnerType.SelectedItem = PartnerTypes.FirstOrDefault(x => x.Value == partner.PartnerType) ?? PartnerTypes[0];
        _invoiceStatus.SelectedItem = InvoiceStatuses.FirstOrDefault(x => x.Value == partner.InvoiceStatus) ?? InvoiceStatuses[^1];
        _registrationNumber.Text = partner.RegistrationNumber ?? "";
        _isActive.IsChecked = partner.IsActive;
        _saveButton.Content = "更新する";
        SetMessage($"{partner.Code} {partner.Name} を編集中です。", false);
    }

    private void ClearForm()
    {
        _editingPartnerId = null;
        _code.Text = "";
        _name.Text = "";
        _partnerType.SelectedIndex = 0;
        _invoiceStatus.SelectedIndex = 0;
        _registrationNumber.Text = "";
        _isActive.IsChecked = true;
        _saveButton.Content = "登録する";
        SetMessage("新しい取引先を入力できます。", false);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text))
        {
            SetMessage("取引先コードと取引先名を入力してください。", true);
            return;
        }

        if (_partnerType.SelectedItem is not PartnerChoice partnerType ||
            _invoiceStatus.SelectedItem is not PartnerChoice invoiceStatus)
        {
            SetMessage("取引先区分とインボイス区分を選択してください。", true);
            return;
        }

        try
        {
            _saveButton.IsEnabled = false;
            if (_editingPartnerId.HasValue)
            {
                await _database.UpdateBusinessPartnerAsync(
                    _user.CompanyId,
                    _editingPartnerId.Value,
                    _code.Text.Trim(),
                    _name.Text.Trim(),
                    partnerType.Value,
                    invoiceStatus.Value,
                    _registrationNumber.Text,
                    _isActive.IsChecked == true);
                SetMessage("取引先を更新しました。", false);
            }
            else
            {
                await _database.CreateBusinessPartnerAsync(
                    _user.CompanyId,
                    _code.Text.Trim(),
                    _name.Text.Trim(),
                    partnerType.Value,
                    invoiceStatus.Value,
                    _registrationNumber.Text,
                    _isActive.IsChecked == true);
                SetMessage("取引先を登録しました。", false);
                ClearForm();
            }

            await LoadPartnersAsync();
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            SetMessage("同じ取引先コードが既に登録されています。", true);
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 275)
        {
            SetMessage("取引先区分またはインボイス区分が不正です。", true);
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

    private static string ToPartnerTypeLabel(string value)
    {
        return PartnerTypes.FirstOrDefault(x => x.Value == value)?.Label ?? value;
    }

    private static string ToInvoiceStatusLabel(string value)
    {
        return InvoiceStatuses.FirstOrDefault(x => x.Value == value)?.Label ?? value;
    }

    private void SetMessage(string text, bool isError)
    {
        _message.Text = text;
        _message.Foreground = isError ? Brush.Parse("#B42318") : Brush.Parse("#4A5568");
    }

    private sealed record PartnerChoice(string Value, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
