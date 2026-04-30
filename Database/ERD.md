# AccountingApp ER図

このER図は `Database/schema.sql` を元にした現在のデータベース構造です。

現在の仕訳データは `journal_vouchers` と `journal_lines` が主系統です。旧方式の `journal_entries` は廃止済みです。

## 主系統

```mermaid
erDiagram
    companies {
        int company_id PK
        varchar name
        date fiscal_year_start
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
        boolean is_control_account
        int default_tax_code_id FK
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

    sub_account_balances {
        bigint balance_id PK
        int company_id FK
        int sub_account_id FK
        int fiscal_year
        int month
        numeric balance
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
        bigint voucher_id PK
        int company_id FK
        date entry_date
        varchar entry_number
        varchar reference
        int created_by FK
        timestamp created_at
        timestamp updated_at
    }

    journal_lines {
        bigint line_id PK
        bigint voucher_id FK
        int company_id FK
        int line_no
        varchar side
        int account_id FK
        int sub_account_id FK
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

    companies ||--o{ user_companies : "所属"
    users ||--o{ user_companies : "所属"

    companies ||--o{ accounts : "保有"
    companies ||--o{ sub_accounts : "保有"
    accounts ||--o{ sub_accounts : "補助科目"
    sub_accounts ||--o{ sub_account_balances : "月次残高"
    companies ||--o{ sub_account_balances : "保有"

    companies ||--o{ tax_codes : "税区分"
    tax_codes ||--o{ accounts : "既定税区分"

    companies ||--o{ business_partners : "取引先"

    companies ||--o{ journal_vouchers : "伝票"
    users ||--o{ journal_vouchers : "作成者"
    journal_vouchers ||--o{ journal_lines : "明細"
    companies ||--o{ journal_lines : "明細"
    accounts ||--o{ journal_lines : "勘定科目"
    sub_accounts ||--o{ journal_lines : "補助科目"
    tax_codes ||--o{ journal_lines : "税区分"
    business_partners ||--o{ journal_lines : "取引先"
```

## 主要な一意制約

- `users.login_id`
- `user_companies(user_id, company_id)`
- `accounts(company_id, code)`
- `sub_accounts(company_id, account_id, code)`
- `tax_codes(company_id, code)`
- `business_partners(company_id, code)`
- `journal_vouchers(company_id, entry_number)`
- `journal_lines(voucher_id, line_no)`
- `sub_account_balances(company_id, sub_account_id, fiscal_year, month)`

## 現在の設計メモ

- 会社ごとに勘定科目、補助科目、税区分、取引先を持ちます。
- ユーザーと会社は `user_companies` で多対多です。
- 仕訳は `journal_vouchers` が伝票ヘッダ、`journal_lines` が借方/貸方の明細です。
- 複合仕訳は `journal_lines` に複数明細としてそのまま保持します。
- 出納帳・仕訳帳・仕訳編集は新方式の仕訳テーブルを参照します。
- インボイス対応項目は `business_partners` と `journal_lines` にあります。
- 旧仕様の `journal_entries` は削除対象です。既存DBでは `schema.sql` 実行時に `DROP TABLE IF EXISTS journal_entries` で撤去します。
