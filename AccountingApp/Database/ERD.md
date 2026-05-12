# AccountingApp ERD

このERDは [schema.sql](/D:/honlabo/SQLiteAcc/AccountingApp/Database/schema.sql:1) の現行定義を元に、AccountingApp が利用している SQLite スキーマを整理したものです。

## ER図

```mermaid
erDiagram
    companies {
        int company_id PK
        varchar name
        date fiscal_year_start
        int closing_day
        varchar tax_entry_method
        boolean is_tax_exempt
        int account_set_id FK
        timestamp created_at
    }

    users {
        int user_id PK
        varchar login_id UK
        varchar display_name
        varchar password_hash
        varchar password_salt
        boolean is_active
        timestamp created_at
        timestamp updated_at
    }

    user_companies {
        int user_id PK, FK
        int company_id PK, FK
        varchar role
        timestamp created_at
    }

    accounts {
        int account_id PK
        int company_id FK
        varchar code
        varchar name
        varchar account_type
        varchar balance_side
        boolean is_control_account
        int default_tax_code_id FK
        boolean is_active
        timestamp created_at
    }

    sub_accounts {
        int sub_account_id PK
        int company_id FK
        int account_id FK
        varchar code
        varchar name
        varchar external_code
        numeric balance
        boolean is_active
        timestamp created_at
    }

    account_sets {
        int account_set_id PK
        varchar name
        int source_company_id FK
        boolean is_active
        timestamp created_at
    }

    account_set_accounts {
        int account_set_account_id PK
        int account_set_id FK
        varchar code
        varchar name
        varchar account_type
        varchar balance_side
        boolean is_control_account
        varchar default_tax_code
        int display_order
        timestamp created_at
    }

    account_set_sub_accounts {
        int account_set_sub_account_id PK
        int account_set_account_id FK
        varchar code
        varchar name
        varchar external_code
        numeric opening_balance
        boolean is_active
        int display_order
        timestamp created_at
    }

    tax_codes {
        int tax_code_id PK
        int company_id FK
        varchar code
        varchar name
        varchar tax_kind
        numeric tax_rate
        boolean is_purchase_credit
        boolean is_taxable
        boolean requires_invoice
        numeric default_purchase_credit_rate
        boolean is_active
        timestamp created_at
    }

    business_partners {
        int partner_id PK
        int company_id FK
        varchar code
        varchar name
        varchar partner_type
        varchar invoice_status
        varchar registration_number
        boolean is_active
        timestamp created_at
        timestamp updated_at
    }

    journal_vouchers {
        int voucher_id PK
        int company_id FK
        date entry_date
        varchar entry_number
        varchar reference
        int created_by FK
        varchar source_type
        varchar source_key
        timestamp created_at
        timestamp updated_at
    }

    journal_lines {
        int line_id PK
        int voucher_id FK
        int company_id FK
        int line_no
        varchar side
        int account_id FK
        int sub_account_id
        numeric amount
        int tax_code_id FK
        numeric tax_rate
        numeric tax_amount
        numeric creditable_tax_amount
        numeric non_creditable_tax_amount
        varchar tax_input_type
        text description
        int partner_id FK
        varchar invoice_number
        varchar invoice_registration_number
        varchar invoice_status
        numeric purchase_credit_rate
        timestamp created_at
        timestamp updated_at
    }

    sub_account_balances {
        int balance_id PK
        int company_id FK
        int sub_account_id FK
        int fiscal_year
        int month
        numeric balance
    }

    annual_carry_forwards {
        int carry_forward_id PK
        int company_id FK
        date source_fiscal_year_start
        date source_fiscal_year_end
        date next_fiscal_year_start
        varchar entry_number
        int equity_account_id FK
        numeric net_income
        int created_by FK
        timestamp created_at
    }

    annual_closings {
        int closing_id PK
        int company_id FK
        date fiscal_year_start
        date fiscal_year_end
        date next_fiscal_year_start
        varchar carry_forward_entry_number
        varchar status
        int closed_by FK
        timestamp closed_at
        int unlocked_by FK
        timestamp unlocked_at
        text unlock_reason
        timestamp created_at
        timestamp updated_at
    }

    operation_logs {
        int log_id PK
        int company_id FK
        int user_id FK
        varchar operation_type
        varchar target_type
        varchar target_key
        text summary
        text metadata_json
        timestamp occurred_at
    }

    companies ||--o{ user_companies : assigns
    users ||--o{ user_companies : belongs_to

    companies ||--o{ accounts : owns
    companies ||--o{ sub_accounts : owns
    accounts ||--o{ sub_accounts : has
    companies ||--o{ tax_codes : owns
    tax_codes ||--o{ accounts : default_tax
    companies ||--o{ business_partners : owns

    account_sets ||--o{ companies : default_set
    companies o|--o{ account_sets : source_company
    account_sets ||--o{ account_set_accounts : has
    account_set_accounts ||--o{ account_set_sub_accounts : has

    companies ||--o{ journal_vouchers : posts
    users ||--o{ journal_vouchers : created
    journal_vouchers ||--o{ journal_lines : contains
    companies ||--o{ journal_lines : scoped
    accounts ||--o{ journal_lines : posted_to
    tax_codes ||--o{ journal_lines : taxed_by
    business_partners ||--o{ journal_lines : related_partner

    sub_accounts ||--o{ sub_account_balances : monthly_balance
    companies ||--o{ sub_account_balances : scoped

    companies ||--o{ annual_carry_forwards : has
    accounts ||--o{ annual_carry_forwards : equity_account
    users ||--o{ annual_carry_forwards : created

    companies ||--o{ annual_closings : has
    users ||--o{ annual_closings : closed_by
    users ||--o{ annual_closings : unlocked_by

    companies ||--o{ operation_logs : has
    users ||--o{ operation_logs : operated
```

## 主なテーブル

- `companies`
  会社の基本設定。SQLite版ではトリガーにより `1 DB = 1 company` に制限されています。
- `users`, `user_companies`
  ユーザー本体と会社への所属。現状は1社前提でも、権限付与テーブルは残っています。
- `accounts`, `sub_accounts`, `tax_codes`, `business_partners`
  日常運用で使うマスタ群です。
- `account_sets`, `account_set_accounts`, `account_set_sub_accounts`
  勘定科目テンプレートを保持するテーブル群です。`companies.account_set_id` で採用中のテンプレートを参照できます。
- `journal_vouchers`, `journal_lines`
  仕訳ヘッダと明細。`journal_vouchers` が親、`journal_lines` が子です。
- `sub_account_balances`
  補助科目ごとの月次残高です。
- `annual_carry_forwards`, `annual_closings`
  年度締め・繰越関連の記録です。
- `operation_logs`
  主要操作の監査ログです。

## 主な制約

- `companies`
  `closing_day` は `1..31`、`tax_entry_method` は `gross | net`。
- `accounts`
  `UNIQUE(company_id, code)`。
  `account_type` は `asset | liability | equity | revenue | expense`。
  `balance_side` は `debit | credit`。
- `sub_accounts`
  `UNIQUE(company_id, account_id, code)`。
- `account_sets`
  `UNIQUE(name)`。
- `account_set_accounts`
  `UNIQUE(account_set_id, code)`。
- `account_set_sub_accounts`
  `UNIQUE(account_set_account_id, code)`。
- `tax_codes`
  `UNIQUE(company_id, code)`。
  `tax_kind` は `sales | purchase | non_taxable | exempt | out_of_scope`。
- `business_partners`
  `UNIQUE(company_id, code)`。
  `partner_type` は `customer | supplier | both | other`。
  `invoice_status` は `qualified | exempt | unregistered | unknown`。
- `journal_vouchers`
  `UNIQUE(company_id, entry_number)`。
  `source_type` は `manual | annual_carry_forward`。
- `journal_lines`
  `UNIQUE(voucher_id, line_no)`。
  `side` は `debit | credit`、`amount > 0`。
- `sub_account_balances`
  `UNIQUE(company_id, sub_account_id, fiscal_year, month)`。
- `annual_carry_forwards`
  `UNIQUE(company_id, next_fiscal_year_start)` と `UNIQUE(company_id, entry_number)`。
- `annual_closings`
  `UNIQUE(company_id, fiscal_year_start)` と `UNIQUE(company_id, next_fiscal_year_start)`。
  `status` は `open | closed`。

## インデックス

- `idx_user_companies_company`
- `idx_accounts_company`
- `idx_sub_accounts_company_account`
- `idx_account_sets_source_company`
- `idx_account_set_accounts_set`
- `idx_account_set_sub_accounts_parent`
- `idx_tax_codes_company`
- `idx_business_partners_company`
- `idx_journal_vouchers_company_date`
- `idx_journal_vouchers_company_number`
- `idx_journal_lines_voucher`
- `idx_journal_lines_company_account`
- `idx_journal_lines_company_partner`
- `idx_accounts_company_active`
- `idx_sub_account_balances_company_period`
- `idx_annual_carry_forwards_company_start`
- `idx_annual_closings_company_year`
- `idx_annual_closings_company_status`
- `idx_journal_vouchers_company_source`
- `idx_operation_logs_company_time`
- `idx_operation_logs_company_target`

## 補足

- `journal_lines.sub_account_id` は外部キー制約を持たず、補助科目なしを `0` で表現します。
- `journal_vouchers.source_type = 'annual_carry_forward'` は年度繰越仕訳を示します。
- `account_set_accounts.default_tax_code` は `tax_codes.tax_code_id` ではなく税区分コード文字列を保持します。
- `account_sets.source_company_id` はテンプレートのコピー元会社を示す任意参照です。
