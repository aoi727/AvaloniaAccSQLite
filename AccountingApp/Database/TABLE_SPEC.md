# AccountingApp テーブル・フィールド簡易仕様

この文書は `Database/schema.sql` をもとに、AccountingApp で使用しているテーブルとフィールドの役割を簡潔に整理したものです。

## 1. 会社・ユーザー

### `companies`
会社の基本設定を保持します。SQLite 版では 1 DB = 1 会社の前提です。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `company_id` | INTEGER | ○ | 会社ID。主キー。 |
| `name` | VARCHAR(100) | ○ | 会社名。 |
| `fiscal_year_start` | DATE | ○ | 会計年度の開始日。 |
| `closing_day` | INTEGER | ○ | 締め日。1〜31。 |
| `tax_entry_method` | VARCHAR(20) | ○ | 税処理方式。`gross` / `net`。 |
| `is_tax_exempt` | BOOLEAN | ○ | 免税事業者か。 |
| `account_set_id` | INTEGER |  | 適用元の勘定科目セットID。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

### `users`
ログインユーザーを保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `user_id` | INTEGER | ○ | ユーザーID。主キー。 |
| `login_id` | VARCHAR(100) | ○ | ログインID。ユニーク。 |
| `display_name` | VARCHAR(100) | ○ | 表示名。 |
| `password_hash` | VARCHAR(200) | ○ | ハッシュ化済みパスワード。 |
| `password_salt` | VARCHAR(200) | ○ | パスワードソルト。 |
| `is_active` | BOOLEAN |  | 有効フラグ。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |
| `updated_at` | TIMESTAMP |  | 更新日時。 |

### `user_companies`
ユーザーと会社の所属関係を表します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `user_id` | INTEGER | ○ | ユーザーID。`users` 参照。 |
| `company_id` | INTEGER | ○ | 会社ID。`companies` 参照。 |
| `role` | VARCHAR(20) | ○ | 会社内ロール。例: `admin`。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

## 2. 勘定科目

### `accounts`
主勘定科目を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `account_id` | INTEGER | ○ | 勘定科目ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `code` | VARCHAR(10) | ○ | 勘定科目コード。会社内ユニーク。 |
| `name` | VARCHAR(100) | ○ | 勘定科目名。 |
| `account_type` | VARCHAR(20) | ○ | 科目区分。`asset` / `liability` / `equity` / `revenue` / `expense`。 |
| `balance_side` | VARCHAR(10) | ○ | 通常残高。`debit` / `credit`。 |
| `is_control_account` | BOOLEAN |  | 補助科目を持つ主科目か。 |
| `default_tax_code_id` | INTEGER |  | 既定税区分ID。 |
| `is_active` | BOOLEAN |  | 有効フラグ。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

### `sub_accounts`
補助科目を保持します。通常は `accounts` 配下にぶら下がります。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `sub_account_id` | INTEGER | ○ | 補助科目ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `account_id` | INTEGER | ○ | 親勘定科目ID。 |
| `code` | VARCHAR(20) | ○ | 補助科目コード。親科目内ユニーク。 |
| `name` | VARCHAR(200) | ○ | 補助科目名。 |
| `external_code` | VARCHAR(50) |  | 外部システム連携用コード。 |
| `balance` | NUMERIC(15,2) |  | 初期残高または補助科目残高の保持用。 |
| `is_active` | BOOLEAN |  | 有効フラグ。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

### `sub_account_balances`
補助科目ごとの月次残高を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `balance_id` | INTEGER | ○ | 残高ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `sub_account_id` | INTEGER | ○ | 補助科目ID。 |
| `fiscal_year` | INTEGER | ○ | 年度。 |
| `month` | INTEGER | ○ | 月。1〜12。 |
| `balance` | NUMERIC(15,2) | ○ | 当月残高。 |

## 3. 勘定科目テンプレート

### `account_sets`
勘定科目セットのヘッダです。会社からテンプレート化したセットを表します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `account_set_id` | INTEGER | ○ | 勘定科目セットID。主キー。 |
| `name` | VARCHAR(100) | ○ | セット名。ユニーク。 |
| `source_company_id` | INTEGER |  | コピー元会社ID。 |
| `is_active` | BOOLEAN |  | 有効フラグ。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

### `account_set_accounts`
勘定科目セット内の主勘定科目を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `account_set_account_id` | INTEGER | ○ | セット内勘定科目ID。主キー。 |
| `account_set_id` | INTEGER | ○ | 勘定科目セットID。 |
| `code` | VARCHAR(10) | ○ | 勘定科目コード。 |
| `name` | VARCHAR(100) | ○ | 勘定科目名。 |
| `account_type` | VARCHAR(20) | ○ | 科目区分。 |
| `balance_side` | VARCHAR(10) | ○ | 通常残高。 |
| `is_control_account` | BOOLEAN |  | 補助科目を持つか。 |
| `default_tax_code` | VARCHAR(20) |  | 既定税区分コード。 |
| `display_order` | INTEGER |  | 表示順。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

### `account_set_sub_accounts`
勘定科目セット内の補助科目を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `account_set_sub_account_id` | INTEGER | ○ | セット内補助科目ID。主キー。 |
| `account_set_account_id` | INTEGER | ○ | 親のセット内勘定科目ID。 |
| `code` | VARCHAR(20) | ○ | 補助科目コード。 |
| `name` | VARCHAR(200) | ○ | 補助科目名。 |
| `external_code` | VARCHAR(50) |  | 外部コード。 |
| `opening_balance` | NUMERIC(15,2) |  | 初期残高。 |
| `is_active` | BOOLEAN |  | 有効フラグ。 |
| `display_order` | INTEGER |  | 表示順。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

## 4. 税区分・取引先

### `tax_codes`
税区分マスタを保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `tax_code_id` | INTEGER | ○ | 税区分ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `code` | VARCHAR(20) | ○ | 税区分コード。会社内ユニーク。 |
| `name` | VARCHAR(100) | ○ | 税区分名。 |
| `tax_kind` | VARCHAR(20) | ○ | 区分。`sales` / `purchase` / `non_taxable` / `exempt` / `out_of_scope`。 |
| `tax_rate` | NUMERIC(5,2) | ○ | 税率。 |
| `is_purchase_credit` | BOOLEAN |  | 仕入税額控除対象か。 |
| `is_taxable` | BOOLEAN |  | 課税対象か。 |
| `requires_invoice` | BOOLEAN |  | インボイス要件があるか。 |
| `default_purchase_credit_rate` | NUMERIC(5,2) |  | 既定の仕入控除率。 |
| `is_active` | BOOLEAN |  | 有効フラグ。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

### `business_partners`
取引先マスタを保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `partner_id` | INTEGER | ○ | 取引先ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `code` | VARCHAR(30) | ○ | 取引先コード。会社内ユニーク。 |
| `name` | VARCHAR(200) | ○ | 取引先名。 |
| `partner_type` | VARCHAR(20) | ○ | 種別。`customer` / `supplier` / `both` / `other`。 |
| `invoice_status` | VARCHAR(20) | ○ | 適格請求書登録状況。`qualified` / `exempt` / `unregistered` / `unknown`。 |
| `registration_number` | VARCHAR(20) |  | 登録番号。 |
| `is_active` | BOOLEAN |  | 有効フラグ。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |
| `updated_at` | TIMESTAMP |  | 更新日時。 |

## 5. 仕訳

### `journal_vouchers`
仕訳伝票のヘッダを保持します。1伝票に複数明細を持ちます。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `voucher_id` | INTEGER | ○ | 伝票ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `entry_date` | DATE | ○ | 伝票日付。 |
| `entry_number` | VARCHAR(20) | ○ | 伝票番号。会社内ユニーク。 |
| `reference` | VARCHAR(100) |  | 参照番号・外部番号など。 |
| `created_by` | INTEGER |  | 作成ユーザーID。 |
| `source_type` | VARCHAR(40) | ○ | 作成元。`manual` / `annual_carry_forward`。 |
| `source_key` | VARCHAR(100) |  | 作成元識別キー。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |
| `updated_at` | TIMESTAMP |  | 更新日時。 |

### `journal_lines`
仕訳伝票の明細行を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `line_id` | INTEGER | ○ | 明細ID。主キー。 |
| `voucher_id` | INTEGER | ○ | 親伝票ID。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `line_no` | INTEGER | ○ | 行番号。伝票内ユニーク。 |
| `side` | VARCHAR(6) | ○ | 借貸区分。`debit` / `credit`。 |
| `account_id` | INTEGER | ○ | 勘定科目ID。 |
| `sub_account_id` | INTEGER |  | 補助科目ID。未指定時は `0` 運用。 |
| `amount` | NUMERIC(15,2) | ○ | 金額。正数のみ。 |
| `tax_code_id` | INTEGER |  | 税区分ID。 |
| `tax_rate` | NUMERIC(5,2) |  | 税率。 |
| `tax_amount` | NUMERIC(15,2) |  | 消費税額。 |
| `creditable_tax_amount` | NUMERIC(15,2) |  | 控除対象税額。 |
| `non_creditable_tax_amount` | NUMERIC(15,2) |  | 控除対象外税額。 |
| `tax_input_type` | VARCHAR(10) |  | 税入力方法。 |
| `description` | TEXT |  | 明細摘要。 |
| `partner_id` | INTEGER |  | 取引先ID。 |
| `invoice_number` | VARCHAR(100) |  | 請求書番号。 |
| `invoice_registration_number` | VARCHAR(20) |  | 登録番号。 |
| `invoice_status` | VARCHAR(20) |  | 明細時点のインボイス状態。 |
| `purchase_credit_rate` | NUMERIC(5,2) |  | 仕入控除率。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |
| `updated_at` | TIMESTAMP |  | 更新日時。 |

## 6. 年次処理

### `annual_carry_forwards`
年度繰越で自動作成した繰越仕訳の記録を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `carry_forward_id` | INTEGER | ○ | 繰越ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `source_fiscal_year_start` | DATE | ○ | 元年度開始日。 |
| `source_fiscal_year_end` | DATE | ○ | 元年度終了日。 |
| `next_fiscal_year_start` | DATE | ○ | 次年度開始日。 |
| `entry_number` | VARCHAR(30) | ○ | 繰越仕訳番号。 |
| `equity_account_id` | INTEGER | ○ | 振替先純資産科目ID。 |
| `net_income` | NUMERIC(15,2) | ○ | 当期純利益・純損失。 |
| `created_by` | INTEGER |  | 作成ユーザーID。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |

### `annual_closings`
年度締めの状態管理を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `closing_id` | INTEGER | ○ | 年度締めID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `fiscal_year_start` | DATE | ○ | 対象年度開始日。 |
| `fiscal_year_end` | DATE | ○ | 対象年度終了日。 |
| `next_fiscal_year_start` | DATE | ○ | 次年度開始日。 |
| `carry_forward_entry_number` | VARCHAR(30) |  | 繰越仕訳番号。 |
| `status` | VARCHAR(20) | ○ | 状態。`open` / `closed`。 |
| `closed_by` | INTEGER |  | 締め実行ユーザーID。 |
| `closed_at` | TIMESTAMP |  | 締め実行日時。 |
| `unlocked_by` | INTEGER |  | 再開放ユーザーID。 |
| `unlocked_at` | TIMESTAMP |  | 再開放日時。 |
| `unlock_reason` | TEXT |  | 再開放理由。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |
| `updated_at` | TIMESTAMP |  | 更新日時。 |

## 7. 予算・資金繰り

### `monthly_budget_plans`
予算実績 / 資金繰り見込画面で入力した月別計画を保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `budget_plan_id` | INTEGER | ○ | 月別計画ID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `fiscal_year_start` | DATE | ○ | 対象年度開始日。 |
| `month_start` | DATE | ○ | 対象月開始日。 |
| `sales_budget` | NUMERIC(15,2) | ○ | 売上予算。 |
| `expense_budget` | NUMERIC(15,2) | ○ | 支出予算。 |
| `expected_cash_in` | NUMERIC(15,2) | ○ | 入金見込。 |
| `expected_cash_out` | NUMERIC(15,2) | ○ | 出金見込。 |
| `note` | TEXT |  | メモ。 |
| `created_at` | TIMESTAMP |  | 作成日時。 |
| `updated_at` | TIMESTAMP |  | 更新日時。 |

## 8. 操作ログ

### `operation_logs`
主要な操作履歴を監査用に保持します。

| フィールド | 型 | 必須 | 説明 |
|---|---|---:|---|
| `log_id` | INTEGER | ○ | ログID。主キー。 |
| `company_id` | INTEGER | ○ | 会社ID。 |
| `user_id` | INTEGER |  | 実行ユーザーID。 |
| `operation_type` | VARCHAR(60) | ○ | 操作種別。 |
| `target_type` | VARCHAR(60) | ○ | 対象種別。 |
| `target_key` | VARCHAR(120) |  | 対象識別子。 |
| `summary` | TEXT | ○ | 操作概要。 |
| `metadata_json` | TEXT |  | 追加情報JSON。 |
| `occurred_at` | TIMESTAMP |  | 発生日時。 |

## 9. 主な関連

- `companies` 1 : N `accounts`, `sub_accounts`, `tax_codes`, `business_partners`, `journal_vouchers`
- `accounts` 1 : N `sub_accounts`
- `journal_vouchers` 1 : N `journal_lines`
- `sub_accounts` 1 : N `sub_account_balances`
- `companies` 1 : N `monthly_budget_plans`
- `users` と `companies` は `user_companies` で関連付け
- 年次処理は `annual_closings` と `annual_carry_forwards` で管理

## 10. 補足

- SQLite 版は `companies` に単一行制約のトリガーがあります。
- 初期勘定科目は `Database/seed_accounts.csv` または初期設定で選んだCSVから投入します。
- 補助科目未指定の仕訳明細は `journal_lines.sub_account_id = 0` で扱う運用です。
