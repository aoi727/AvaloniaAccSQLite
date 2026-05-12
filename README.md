# SQLiteAcc

SQLite ベースの会計アプリ群です。

## AccountingApp

`AccountingApp` は、通常の会計入力、マスタ管理、帳票、決算処理を行う親アプリです。
このアプリ単体で利用できます。

主な機能:

- 勘定科目、補助科目、税区分、取引先の管理
- 仕訳入力、定型仕訳、証憑添付
- 仕訳帳、総勘定元帳、現金出納帳
- 試算表、貸借対照表、損益計算書、消費税集計表
- 月次ロック、年度締め、繰越仕訳作成

## ReligiousReportApp

`ReligiousReportApp` は、宗教法人向けの運営収支報告書を作成する拡張アプリです。
`AccountingApp` が作成した SQLite DB ファイルを選択して利用します。

この拡張アプリは `AccountingApp` の既存会計テーブルを読み取り専用で参照し、宗教法人向けの追加情報だけを `religious_report_*` テーブルに保存します。
そのため、`AccountingApp` は単体アプリとして完結したまま利用できます。

主な機能:

- 運営収支分類マスタ
- 勘定科目の役割設定
- 任意期間の入出金レビュー
- 複合仕訳の相手明細別レビュー
- レビュー後の元仕訳変更検知
- 期間の確認済み・確定ロック
- 前期繰越・次期繰越の表示
- 年度別・分類別予算入力
- 運営収支報告書プレビュー
- 運営収支報告書の PDF 出力

## Project Layout

```text
SQLiteAcc/
  AccountingApp/
  ReligiousReportApp/
  docs/
```

## Build

```powershell
dotnet build .\AccountingApp\AccountingApp.csproj
dotnet build .\ReligiousReportApp\ReligiousReportApp.csproj
```

## DB Contract

`ReligiousReportApp` が依存する DB テーブルと書き込み範囲は [docs/db-contract.md](docs/db-contract.md) を参照してください。
