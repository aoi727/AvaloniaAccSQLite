# DB Contract

`ReligiousReportApp` は、`AccountingApp` が作成した SQLite DB ファイルを開いて利用します。
親アプリ側の会計テーブルは読み取り専用で参照し、拡張アプリ固有の情報だけを `religious_report_*` テーブルに保存します。

## Read-Only Accounting Tables

`ReligiousReportApp` が参照する主な `AccountingApp` テーブル:

- `companies`
- `accounts`
- `journal_vouchers`
- `journal_lines`

## Religious Report Tables

`ReligiousReportApp` が作成・更新するテーブル:

- `religious_report_categories`
- `religious_report_account_roles`
- `religious_report_cash_flow_split_overrides`
- `religious_report_cash_flow_overrides`
- `religious_report_period_reviews`
- `religious_report_carryovers`
- `religious_report_notes`
- `religious_report_budgets`
- `religious_report_account_mappings`

## Boundary

- `AccountingApp` は `religious_report_*` テーブルを利用しません。
- `ReligiousReportApp` は `religious_report_*` 以外のテーブルを更新しません。
- 仕訳入力、勘定科目登録、取引先管理、決算処理は `AccountingApp` で行います。
- 運営収支報告書向けの分類、レビュー、予算、注記、PDF 出力は `ReligiousReportApp` で行います。
