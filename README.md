# AvaloniaAccSQLite

Avalonia UI と SQLite を使ったデスクトップ会計アプリです。仕訳入力、元帳、現金出納帳、試算表、貸借対照表、損益計算書、補助科目や取引先管理までをひととおり扱える構成になっています。

## 主な特徴

- Avalonia ベースのデスクトップ UI
- SQLite を使ったローカル保存
- 勘定科目、補助科目、税区分、取引先の管理
- 仕訳帳、総勘定元帳、現金出納帳の参照
- 試算表、貸借対照表、損益計算書の出力
- 初期科目データとスキーマ SQL を同梱

## 動作環境

- .NET 10 SDK
- Windows x64 を想定

主要な依存関係:

- `Avalonia 12.0.1`
- `Microsoft.Data.Sqlite 10.0.7`

## セットアップ

1. `appsettings.example.json` を `appsettings.json` にコピーします。
2. SQLite ファイルの保存先を設定します。

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=accounting_app.db"
  }
}
```

環境変数 `ACCOUNTING_APP_CONNECTION` を使って接続先を上書きすることもできます。

```powershell
$env:ACCOUNTING_APP_CONNECTION="Data Source=accounting_app.db"
```

## 実行方法

```powershell
dotnet run --project AccountingApp.csproj -p:UsedAvaloniaProducts=
```

## 配布用ビルド

```powershell
dotnet publish AccountingApp.csproj -c Release
```

`Release` 構成では単一ファイル配布、トリミング、自己完結配布の設定が入っています。

## データベース関連

- スキーマ: `Database/schema.sql`
- 初期科目データ: `Database/seed_accounts.csv`
- シード SQL: `Database/seed_accounts_subaccounts_from_csv.sql`
- ER 図メモ: `Database/ERD.md`

## ディレクトリ構成

- `Views/`: 画面 UI
- `Models/`: 画面・帳票用モデル
- `Data/`: SQLite アクセスと帳票出力
- `Database/`: スキーマ、シード、ERD
- `Styles/`: Avalonia スタイル

## 補足

`appsettings.json` はローカル環境ごとの差分が出やすいため、リポジトリには `appsettings.example.json` を含めています。
