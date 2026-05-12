import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const outputDir = path.resolve("outputs", "accountingapp-erd");
const outputPath = path.join(outputDir, "AccountingApp_ERD.xlsx");

const workbook = Workbook.create();

const colors = {
  title: "#17324D",
  header: "#1F6F5B",
  band: "#EAF4F0",
  section: "#EEF2F7",
  text: "#172033",
};

const tables = [
  ["companies", "会社", "会社・会計年度の基点", "company_id", ""],
  ["users", "ユーザー", "ログインユーザー", "user_id", "login_id"],
  ["user_companies", "ユーザー会社所属", "ユーザーと会社の多対多・権限", "user_id + company_id", "user_id, company_id"],
  ["accounts", "勘定科目", "会社別の勘定科目マスタ", "account_id", "company_id, code"],
  ["sub_accounts", "補助科目", "勘定科目に紐づく補助科目", "sub_account_id", "company_id, account_id, code"],
  ["tax_codes", "税区分", "消費税・インボイス計算用の税区分", "tax_code_id", "company_id, code"],
  ["business_partners", "取引先", "得意先・仕入先とインボイス登録情報", "partner_id", "company_id, code"],
  ["journal_vouchers", "仕訳伝票", "仕訳ヘッダ。現在の主系統", "voucher_id", "company_id, entry_number"],
  ["journal_lines", "仕訳明細", "借方・貸方の仕訳明細。現在の主系統", "line_id", "voucher_id, line_no"],
  ["sub_account_balances", "補助科目残高", "補助科目の月次残高", "balance_id", "company_id, sub_account_id, fiscal_year, month"],
];

const columns = [
  ["companies", "company_id", "INTEGER", "PK", "NO", "会社ID"],
  ["companies", "name", "VARCHAR(100)", "", "NO", "会社名"],
  ["companies", "fiscal_year_start", "DATE", "", "NO", "会計年度開始日"],
  ["companies", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],

  ["users", "user_id", "INTEGER", "PK", "NO", "ユーザーID"],
  ["users", "login_id", "VARCHAR(100)", "UNIQUE", "NO", "ログインID"],
  ["users", "display_name", "VARCHAR(100)", "", "NO", "表示名"],
  ["users", "password_hash", "VARCHAR(200)", "", "NO", "パスワードハッシュ"],
  ["users", "password_salt", "VARCHAR(200)", "", "NO", "パスワードソルト"],
  ["users", "is_active", "BOOLEAN", "DEFAULT TRUE", "YES", "有効フラグ"],
  ["users", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],
  ["users", "updated_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "更新日時"],

  ["user_companies", "user_id", "INTEGER", "PK/FK users", "NO", "ユーザーID"],
  ["user_companies", "company_id", "INTEGER", "PK/FK companies", "NO", "会社ID"],
  ["user_companies", "role", "VARCHAR(20)", "", "NO", "会社内ロール"],
  ["user_companies", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],

  ["accounts", "account_id", "INTEGER", "PK", "NO", "勘定科目ID"],
  ["accounts", "company_id", "INTEGER", "FK companies", "NO", "会社ID"],
  ["accounts", "code", "VARCHAR(10)", "UNIQUE part", "NO", "勘定科目コード"],
  ["accounts", "name", "VARCHAR(100)", "", "NO", "勘定科目名"],
  ["accounts", "account_type", "VARCHAR(20)", "CHECK", "NO", "asset/liability/equity/revenue/expense"],
  ["accounts", "is_control_account", "BOOLEAN", "DEFAULT FALSE", "YES", "補助科目を持つか"],
  ["accounts", "default_tax_code_id", "INTEGER", "FK tax_codes", "YES", "既定税区分"],
  ["accounts", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],

  ["sub_accounts", "sub_account_id", "INTEGER", "PK", "NO", "補助科目ID"],
  ["sub_accounts", "company_id", "INTEGER", "FK companies", "NO", "会社ID"],
  ["sub_accounts", "account_id", "INTEGER", "FK accounts", "NO", "親勘定科目ID"],
  ["sub_accounts", "code", "VARCHAR(20)", "UNIQUE part", "NO", "補助科目コード"],
  ["sub_accounts", "name", "VARCHAR(200)", "", "NO", "補助科目名"],
  ["sub_accounts", "external_code", "VARCHAR(50)", "", "YES", "外部コード"],
  ["sub_accounts", "balance", "NUMERIC(15,2)", "DEFAULT 0", "YES", "初期残高"],
  ["sub_accounts", "is_active", "BOOLEAN", "DEFAULT TRUE", "YES", "有効フラグ"],
  ["sub_accounts", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],

  ["tax_codes", "tax_code_id", "INTEGER", "PK", "NO", "税区分ID"],
  ["tax_codes", "company_id", "INTEGER", "FK companies", "NO", "会社ID"],
  ["tax_codes", "code", "VARCHAR(20)", "UNIQUE part", "NO", "税区分コード"],
  ["tax_codes", "name", "VARCHAR(100)", "", "NO", "税区分名"],
  ["tax_codes", "tax_kind", "VARCHAR(20)", "CHECK", "NO", "sales/purchase/non_taxable/exempt/out_of_scope"],
  ["tax_codes", "tax_rate", "NUMERIC(5,2)", "", "NO", "税率"],
  ["tax_codes", "is_purchase_credit", "BOOLEAN", "DEFAULT FALSE", "YES", "仕入税額控除対象"],
  ["tax_codes", "is_taxable", "BOOLEAN", "DEFAULT TRUE", "YES", "課税対象"],
  ["tax_codes", "requires_invoice", "BOOLEAN", "DEFAULT FALSE", "YES", "インボイス判定が必要"],
  ["tax_codes", "default_purchase_credit_rate", "NUMERIC(5,2)", "DEFAULT 0", "YES", "既定控除率"],
  ["tax_codes", "is_active", "BOOLEAN", "DEFAULT TRUE", "YES", "有効フラグ"],
  ["tax_codes", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],

  ["business_partners", "partner_id", "INTEGER", "PK", "NO", "取引先ID"],
  ["business_partners", "company_id", "INTEGER", "FK companies", "NO", "会社ID"],
  ["business_partners", "code", "VARCHAR(30)", "UNIQUE part", "NO", "取引先コード"],
  ["business_partners", "name", "VARCHAR(200)", "", "NO", "取引先名"],
  ["business_partners", "partner_type", "VARCHAR(20)", "CHECK / DEFAULT supplier", "NO", "customer/supplier/both/other"],
  ["business_partners", "invoice_status", "VARCHAR(20)", "CHECK / DEFAULT unknown", "NO", "qualified/exempt/unregistered/unknown"],
  ["business_partners", "registration_number", "VARCHAR(20)", "", "YES", "適格請求書登録番号"],
  ["business_partners", "is_active", "BOOLEAN", "DEFAULT TRUE", "YES", "有効フラグ"],
  ["business_partners", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],
  ["business_partners", "updated_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "更新日時"],

  ["journal_vouchers", "voucher_id", "BIGINT", "PK", "NO", "伝票ID"],
  ["journal_vouchers", "company_id", "INTEGER", "FK companies", "NO", "会社ID"],
  ["journal_vouchers", "entry_date", "DATE", "", "NO", "取引日"],
  ["journal_vouchers", "entry_number", "VARCHAR(20)", "UNIQUE part", "NO", "伝票番号"],
  ["journal_vouchers", "reference", "VARCHAR(100)", "", "YES", "証憑番号・参照番号"],
  ["journal_vouchers", "created_by", "INTEGER", "FK users", "YES", "作成者"],
  ["journal_vouchers", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],
  ["journal_vouchers", "updated_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "更新日時"],

  ["journal_lines", "line_id", "BIGINT", "PK", "NO", "明細ID"],
  ["journal_lines", "voucher_id", "BIGINT", "FK journal_vouchers ON DELETE CASCADE", "NO", "伝票ID"],
  ["journal_lines", "company_id", "INTEGER", "FK companies", "NO", "会社ID"],
  ["journal_lines", "line_no", "INTEGER", "UNIQUE part", "NO", "伝票内行番号"],
  ["journal_lines", "side", "VARCHAR(6)", "CHECK", "NO", "debit/credit"],
  ["journal_lines", "account_id", "INTEGER", "FK accounts", "NO", "勘定科目ID"],
  ["journal_lines", "sub_account_id", "INTEGER", "FK sub_accounts", "YES", "補助科目ID"],
  ["journal_lines", "amount", "NUMERIC(15,2)", "CHECK amount > 0", "NO", "金額"],
  ["journal_lines", "tax_code_id", "INTEGER", "FK tax_codes", "YES", "税区分ID"],
  ["journal_lines", "tax_rate", "NUMERIC(5,2)", "", "YES", "適用税率"],
  ["journal_lines", "tax_amount", "NUMERIC(15,2)", "DEFAULT 0", "YES", "消費税額"],
  ["journal_lines", "creditable_tax_amount", "NUMERIC(15,2)", "DEFAULT 0", "YES", "控除可能税額"],
  ["journal_lines", "non_creditable_tax_amount", "NUMERIC(15,2)", "DEFAULT 0", "YES", "控除不可税額"],
  ["journal_lines", "tax_input_type", "VARCHAR(10)", "DEFAULT excluded", "YES", "excluded/included"],
  ["journal_lines", "description", "TEXT", "", "YES", "摘要"],
  ["journal_lines", "partner_id", "INTEGER", "FK business_partners", "YES", "取引先ID"],
  ["journal_lines", "invoice_number", "VARCHAR(100)", "", "YES", "請求書番号"],
  ["journal_lines", "invoice_registration_number", "VARCHAR(20)", "", "YES", "明細保存時の登録番号"],
  ["journal_lines", "invoice_status", "VARCHAR(20)", "", "YES", "明細保存時のインボイス区分"],
  ["journal_lines", "purchase_credit_rate", "NUMERIC(5,2)", "", "YES", "仕入税額控除率"],
  ["journal_lines", "created_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "作成日時"],
  ["journal_lines", "updated_at", "TIMESTAMP", "DEFAULT CURRENT_TIMESTAMP", "YES", "更新日時"],

  ["sub_account_balances", "balance_id", "BIGINT", "PK", "NO", "残高ID"],
  ["sub_account_balances", "company_id", "INTEGER", "FK companies", "NO", "会社ID"],
  ["sub_account_balances", "sub_account_id", "INTEGER", "FK sub_accounts", "NO", "補助科目ID"],
  ["sub_account_balances", "fiscal_year", "INTEGER", "UNIQUE part", "NO", "年度"],
  ["sub_account_balances", "month", "INTEGER", "CHECK 1-12 / UNIQUE part", "NO", "月"],
  ["sub_account_balances", "balance", "NUMERIC(15,2)", "", "NO", "残高"],
];

const relations = [
  ["companies.company_id", "user_companies.company_id", "1:N", "会社にユーザー所属が紐づく"],
  ["users.user_id", "user_companies.user_id", "1:N", "ユーザーに会社所属が紐づく"],
  ["companies.company_id", "accounts.company_id", "1:N", "会社別勘定科目"],
  ["companies.company_id", "sub_accounts.company_id", "1:N", "会社別補助科目"],
  ["accounts.account_id", "sub_accounts.account_id", "1:N", "勘定科目に補助科目が紐づく"],
  ["sub_accounts.sub_account_id", "sub_account_balances.sub_account_id", "1:N", "補助科目の月次残高"],
  ["companies.company_id", "tax_codes.company_id", "1:N", "会社別税区分"],
  ["tax_codes.tax_code_id", "accounts.default_tax_code_id", "1:N", "勘定科目の既定税区分"],
  ["companies.company_id", "business_partners.company_id", "1:N", "会社別取引先"],
  ["companies.company_id", "journal_vouchers.company_id", "1:N", "会社別仕訳伝票"],
  ["users.user_id", "journal_vouchers.created_by", "1:N", "伝票作成者"],
  ["journal_vouchers.voucher_id", "journal_lines.voucher_id", "1:N", "伝票ヘッダと明細"],
  ["companies.company_id", "journal_lines.company_id", "1:N", "会社別仕訳明細"],
  ["accounts.account_id", "journal_lines.account_id", "1:N", "明細の勘定科目"],
  ["sub_accounts.sub_account_id", "journal_lines.sub_account_id", "1:N", "明細の補助科目"],
  ["tax_codes.tax_code_id", "journal_lines.tax_code_id", "1:N", "明細の税区分"],
  ["business_partners.partner_id", "journal_lines.partner_id", "1:N", "明細の取引先"],
];

const constraints = [
  ["UNIQUE", "users.login_id", "ログインIDは全体で一意"],
  ["PRIMARY KEY", "user_companies(user_id, company_id)", "ユーザーと会社の所属は1組1件"],
  ["UNIQUE", "accounts(company_id, code)", "会社内で勘定科目コード一意"],
  ["CHECK", "accounts.account_type", "asset/liability/equity/revenue/expense"],
  ["UNIQUE", "sub_accounts(company_id, account_id, code)", "親勘定科目内で補助科目コード一意"],
  ["UNIQUE", "tax_codes(company_id, code)", "会社内で税区分コード一意"],
  ["CHECK", "tax_codes.tax_kind", "sales/purchase/non_taxable/exempt/out_of_scope"],
  ["UNIQUE", "business_partners(company_id, code)", "会社内で取引先コード一意"],
  ["CHECK", "business_partners.partner_type", "customer/supplier/both/other"],
  ["CHECK", "business_partners.invoice_status", "qualified/exempt/unregistered/unknown"],
  ["UNIQUE", "journal_vouchers(company_id, entry_number)", "会社内で伝票番号一意"],
  ["UNIQUE", "journal_lines(voucher_id, line_no)", "伝票内で明細行番号一意"],
  ["CHECK", "journal_lines.side", "debit/credit"],
  ["CHECK", "journal_lines.amount", "金額は0より大きい"],
  ["UNIQUE", "sub_account_balances(company_id, sub_account_id, fiscal_year, month)", "補助科目の月次残高一意"],
  ["CHECK", "sub_account_balances.month", "1から12"],
];

const indexes = [
  ["idx_user_companies_company", "user_companies(company_id)", "会社所属検索"],
  ["idx_accounts_company", "accounts(company_id)", "会社別勘定科目検索"],
  ["idx_sub_accounts_company_account", "sub_accounts(company_id, account_id)", "会社・親科目別補助科目検索"],
  ["idx_tax_codes_company", "tax_codes(company_id)", "会社別税区分検索"],
  ["idx_business_partners_company", "business_partners(company_id)", "会社別取引先検索"],
  ["idx_journal_vouchers_company_date", "journal_vouchers(company_id, entry_date)", "仕訳帳・月次表示"],
  ["idx_journal_vouchers_company_number", "journal_vouchers(company_id, entry_number)", "伝票番号検索"],
  ["idx_journal_lines_voucher", "journal_lines(voucher_id, line_no)", "伝票明細表示"],
  ["idx_journal_lines_company_account", "journal_lines(company_id, account_id, sub_account_id)", "科目別検索"],
  ["idx_journal_lines_company_partner", "journal_lines(company_id, partner_id)", "取引先別検索"],
  ["idx_sub_account_balances_company_period", "sub_account_balances(company_id, fiscal_year, month)", "月次残高検索"],
];

const notes = [
  ["現在の主系統", "仕訳は journal_vouchers と journal_lines を使用します。journal_vouchers が伝票ヘッダ、journal_lines が借方・貸方の明細です。"],
  ["複合仕訳", "複数の借方・貸方明細を journal_lines にそのまま保存するため、自動車下取りなどの複雑な仕訳も同一伝票内で表現できます。"],
  ["旧仕訳", "journal_entries は廃止済みです。既存DBでは schema.sql 実行時に DROP TABLE IF EXISTS journal_entries で撤去します。"],
  ["インボイス対応", "取引先マスタ business_partners に登録番号や区分を保持し、仕訳明細 journal_lines に明細保存時点の請求書番号・登録番号・控除率を保持します。"],
  ["税額整理", "journal_lines には税額、控除可能税額、控除不可税額、税入力方式、仕入税額控除率を保持します。"],
  ["出納帳・仕訳帳", "現在の画面は新方式の伝票・明細テーブルを参照する構成です。仕訳帳は月単位表示と編集後リロードに対応しています。"],
];

function addSheet(name) {
  const sheet = workbook.worksheets.add(name);
  sheet.showGridLines = false;
  return sheet;
}

function title(sheet, text, range) {
  const r = sheet.getRange(range);
  r.merge();
  r.values = [[text]];
  r.format = {
    fill: colors.title,
    font: { bold: true, color: "#FFFFFF", size: 16 },
    horizontalAlignment: "left",
    verticalAlignment: "center",
    rowHeightPx: 32,
  };
}

function colName(index) {
  let n = index + 1;
  let name = "";
  while (n > 0) {
    const rem = (n - 1) % 26;
    name = String.fromCharCode(65 + rem) + name;
    n = Math.floor((n - 1) / 26);
  }
  return name;
}

function writeTable(sheet, startRow, startCol, headers, rows, tableName, widths) {
  const data = [headers, ...rows];
  const range = sheet.getRangeByIndexes(startRow, startCol, data.length, headers.length);
  range.values = data;
  range.format.wrapText = true;
  range.format.verticalAlignment = "top";
  const header = sheet.getRangeByIndexes(startRow, startCol, 1, headers.length);
  header.format = {
    fill: colors.header,
    font: { bold: true, color: "#FFFFFF" },
    horizontalAlignment: "center",
    verticalAlignment: "center",
  };
  const startAddress = `${colName(startCol)}${startRow + 1}`;
  const endAddress = `${colName(startCol + headers.length - 1)}${startRow + data.length}`;
  const table = sheet.tables.add(`${startAddress}:${endAddress}`, true, tableName);
  table.style = "TableStyleMedium4";
  widths.forEach((width, index) => {
    sheet.getRangeByIndexes(startRow, startCol + index, data.length, 1).format.columnWidthPx = width;
  });
}

function addSection(sheet, cell, text) {
  const r = sheet.getRange(cell);
  r.values = [[text]];
  r.format = {
    fill: colors.section,
    font: { bold: true, color: colors.text },
  };
}

const overview = addSheet("概要");
title(overview, "AccountingApp データベース構造", "A1:H1");
overview.getRange("A3:B10").values = [
  ["項目", "内容"],
  ["対象", "AccountingApp / SQLite3"],
  ["主系統", "journal_vouchers / journal_lines"],
  ["旧系統", "journal_entries は廃止済み"],
  ["会社別マスタ", "勘定科目、補助科目、税区分、取引先を会社ごとに保持"],
  ["インボイス対応", "取引先マスタと仕訳明細に登録番号、請求書番号、控除率、インボイス区分を保持"],
  ["作成元", "AccountingApp/Database/schema.sql"],
  ["シート構成", "テーブル一覧、カラム定義、リレーション、制約・索引・メモ"],
];
overview.getRange("A3:B3").format = { fill: colors.header, font: { bold: true, color: "#FFFFFF" } };
overview.getRange("A4:A10").format = { fill: colors.band, font: { bold: true } };
overview.getRange("A3:B10").format.wrapText = true;
overview.getRange("A1:H12").format.verticalAlignment = "top";
overview.getRange("A:A").format.columnWidthPx = 170;
overview.getRange("B:B").format.columnWidthPx = 620;
overview.freezePanes.freezeRows(3);

const tableSheet = addSheet("テーブル一覧");
title(tableSheet, "テーブル一覧", "A1:E1");
writeTable(tableSheet, 2, 0, ["テーブル", "日本語名", "用途", "主キー", "主な一意制約"], tables, "Tables", [190, 170, 400, 190, 320]);
tableSheet.freezePanes.freezeRows(3);

const columnSheet = addSheet("カラム定義");
title(columnSheet, "カラム定義", "A1:F1");
writeTable(columnSheet, 2, 0, ["テーブル", "カラム", "型", "キー/制約", "NULL", "説明"], columns, "Columns", [190, 240, 170, 260, 70, 430]);
columnSheet.freezePanes.freezeRows(3);

const relationSheet = addSheet("リレーション");
title(relationSheet, "リレーション", "A1:D1");
writeTable(relationSheet, 2, 0, ["親", "子", "関係", "説明"], relations, "Relations", [280, 330, 80, 500]);
relationSheet.freezePanes.freezeRows(3);

const constraintSheet = addSheet("制約・索引・メモ");
title(constraintSheet, "制約・索引・設計メモ", "A1:C1");
addSection(constraintSheet, "A3", "制約");
writeTable(constraintSheet, 3, 0, ["種類", "対象", "説明"], constraints, "Constraints", [130, 450, 520]);
const indexStart = constraints.length + 7;
addSection(constraintSheet, `A${indexStart}`, "索引");
writeTable(constraintSheet, indexStart, 0, ["索引名", "対象", "用途"], indexes, "Indexes", [320, 440, 330]);
const noteStart = indexStart + indexes.length + 4;
addSection(constraintSheet, `A${noteStart}`, "設計メモ");
writeTable(constraintSheet, noteStart, 0, ["項目", "メモ"], notes, "DesignNotes", [190, 850]);
constraintSheet.freezePanes.freezeRows(3);

for (const name of ["概要", "テーブル一覧", "カラム定義", "リレーション", "制約・索引・メモ"]) {
  const sheet = workbook.worksheets.getItem(name);
  const used = sheet.getUsedRange();
  if (used) {
    used.format.verticalAlignment = "top";
  }
}

await fs.mkdir(outputDir, { recursive: true });

const overviewInspect = await workbook.inspect({
  kind: "table",
  range: "概要!A1:B10",
  include: "values",
  tableMaxRows: 10,
  tableMaxCols: 2,
});
console.log(overviewInspect.ndjson);

const columnInspect = await workbook.inspect({
  kind: "table",
  range: "カラム定義!A1:F18",
  include: "values",
  tableMaxRows: 18,
  tableMaxCols: 6,
});
console.log(columnInspect.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 100 },
  summary: "formula error scan",
});
console.log(errors.ndjson);

for (const sheetName of ["概要", "テーブル一覧", "カラム定義", "リレーション", "制約・索引・メモ"]) {
  await workbook.render({ sheetName, autoCrop: "all", scale: 1, format: "png" });
}

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);
console.log(outputPath);
