-- 現在の科目CSVをもとに、accounts と sub_accounts を再投入するためのSQLです。
-- 前提:
-- 1. companies / tax_codes は作成済み
-- 2. tax_codes には既定コード SALES10 / PURCHASE10 / NONTAX / OUT が存在する
-- 3. すべての勘定科目に既定補助科目 code='0' を作成する方針
--
-- 使い方:
-- - 手動実行する場合は __COMPANY_ID__ を対象の company_id に置換してから実行してください。

CREATE TEMP TABLE source_accounts (
    code VARCHAR(10) NOT NULL,
    name VARCHAR(100) NOT NULL,
    account_type VARCHAR(20) NOT NULL,
    tax_code_code VARCHAR(20) NOT NULL
) ON COMMIT DROP;

INSERT INTO source_accounts (code, name, account_type, tax_code_code)
VALUES
    ('1018', '現金', 'asset', 'OUT'),
    ('1087', '小口現金', 'asset', 'OUT'),
    ('1317', '普通預金', 'asset', 'OUT'),
    ('1410', '定期預金', 'asset', 'OUT'),
    ('1520', '売掛金', 'asset', 'OUT'),
    ('1623', '商品', 'asset', 'OUT'),
    ('1805', '仮払消費税', 'asset', 'OUT'),
    ('1812', '仮払金', 'asset', 'OUT'),
    ('1829', '立替金', 'asset', 'OUT'),
    ('1867', '貸付金', 'asset', 'OUT'),
    ('1984', '貸倒引当金', 'asset', 'OUT'),
    ('2019', '建物', 'asset', 'PURCHASE10'),
    ('2033', '建物附属設備', 'asset', 'PURCHASE10'),
    ('2057', '構築物', 'asset', 'PURCHASE10'),
    ('2095', '機械装置', 'asset', 'PURCHASE10'),
    ('2143', '車両運搬具', 'asset', 'PURCHASE10'),
    ('2174', '工具器具備品', 'asset', 'PURCHASE10'),
    ('2291', '減価償却累計額', 'asset', 'OUT'),
    ('2318', '土地', 'asset', 'NONTAX'),
    ('2387', '建設仮勘定', 'asset', 'OUT'),
    ('2655', '長期貸付金', 'asset', 'OUT'),
    ('2789', '貸倒引当金', 'asset', 'OUT'),
    ('2923', '創立費', 'asset', 'OUT'),
    ('2947', '開業費', 'asset', 'OUT'),
    ('3010', '支払手形', 'liability', 'OUT'),
    ('3034', '買掛金', 'liability', 'OUT'),
    ('3113', '借入金', 'liability', 'OUT'),
    ('3223', '未払金', 'liability', 'OUT'),
    ('3272', '未払費用', 'liability', 'OUT'),
    ('3278', '未払費用', 'liability', 'OUT'),
    ('3357', '預り金', 'liability', 'OUT'),
    ('3364', '仮受金', 'liability', 'OUT'),
    ('3371', '仮受消費税', 'liability', 'OUT'),
    ('3711', '長期借入金', 'liability', 'OUT'),
    ('4011', '売上高', 'revenue', 'SALES10'),
    ('4028', '売上２', 'revenue', 'SALES10'),
    ('4523', '仕入高', 'expense', 'PURCHASE10'),
    ('4970', '期首商品棚卸高', 'expense', 'OUT'),
    ('4987', '期末商品棚卸高', 'expense', 'OUT'),
    ('6525', '給与手当', 'expense', 'OUT'),
    ('6532', '法定福利費', 'expense', 'PURCHASE10'),
    ('6563', '福利厚生費', 'expense', 'PURCHASE10'),
    ('6628', '広告宣伝費', 'expense', 'PURCHASE10'),
    ('6635', '燃料費', 'expense', 'PURCHASE10'),
    ('6642', '車両維持費', 'expense', 'PURCHASE10'),
    ('6659', '旅費交通費', 'expense', 'PURCHASE10'),
    ('6714', '通信費', 'expense', 'PURCHASE10'),
    ('6721', '水道光熱費', 'expense', 'PURCHASE10'),
    ('6745', '消耗品費', 'expense', 'PURCHASE10'),
    ('6752', '修繕費', 'expense', 'PURCHASE10'),
    ('6769', '保険料', 'expense', 'NONTAX'),
    ('6776', '事務用品費', 'expense', 'PURCHASE10'),
    ('6790', '会議費', 'expense', 'PURCHASE10'),
    ('6800', '支払消費税', 'expense', 'OUT'),
    ('6817', '租税公課', 'expense', 'OUT'),
    ('6824', '交際接待費', 'expense', 'PURCHASE10'),
    ('6831', '地代家賃', 'expense', 'PURCHASE10'),
    ('6848', '減価償却費', 'expense', 'OUT'),
    ('6862', '賃借料', 'expense', 'PURCHASE10'),
    ('6879', '支払手数料', 'expense', 'OUT'),
    ('6886', '報酬料金', 'expense', 'OUT'),
    ('6893', '諸会費', 'expense', 'OUT'),
    ('6910', '事務費', 'expense', 'PURCHASE10'),
    ('6996', '雑費', 'expense', 'PURCHASE10'),
    ('8015', '資本金', 'equity', 'OUT'),
    ('9023', '受取利息', 'revenue', 'OUT'),
    ('9030', '受取配当金', 'revenue', 'OUT'),
    ('9054', '雑収入', 'revenue', 'OUT'),
    ('9061', '受取消費税', 'revenue', 'OUT'),
    ('9315', '固定資産売却益', 'revenue', 'OUT'),
    ('9511', '支払利息', 'expense', 'OUT'),
    ('9566', '雑損失', 'expense', 'OUT'),
    ('9999', '諸口', 'expense', 'OUT');

WITH settings AS (
    SELECT CAST(__COMPANY_ID__ AS INTEGER) AS company_id
)
INSERT INTO accounts (
    company_id,
    code,
    name,
    account_type,
    balance_side,
    is_control_account,
    default_tax_code_id
)
SELECT settings.company_id,
       src.code,
       src.name,
       src.account_type,
       CASE
           WHEN src.account_type IN ('asset', 'expense') THEN 'debit'
           ELSE 'credit'
       END,
       TRUE,
       tc.tax_code_id
FROM source_accounts src
CROSS JOIN settings
JOIN tax_codes tc
  ON tc.company_id = settings.company_id
 AND tc.code = src.tax_code_code
ON CONFLICT (company_id, code) DO UPDATE
SET name = EXCLUDED.name,
    account_type = EXCLUDED.account_type,
    balance_side = EXCLUDED.balance_side,
    is_control_account = EXCLUDED.is_control_account,
    default_tax_code_id = EXCLUDED.default_tax_code_id;

WITH settings AS (
    SELECT CAST(__COMPANY_ID__ AS INTEGER) AS company_id
)
INSERT INTO sub_accounts (
    company_id,
    account_id,
    code,
    name,
    external_code,
    balance,
    is_active
)
SELECT settings.company_id,
       a.account_id,
       '0',
       src.name,
       NULL,
       0,
       TRUE
FROM source_accounts src
CROSS JOIN settings
JOIN accounts a
  ON a.company_id = settings.company_id
 AND a.code = src.code
ON CONFLICT (company_id, account_id, code) DO UPDATE
SET name = EXCLUDED.name,
    is_active = TRUE;
