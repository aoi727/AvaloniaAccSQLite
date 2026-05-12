-- Generic import helper for accounts/sub_accounts.
-- This script intentionally does not embed any fixed account codes.
-- The canonical source of account definitions is `Database/seed_accounts.csv`
-- or a user-selected CSV consumed by the application.
--
-- Usage outline:
-- 1. Prepare `companies` and `tax_codes` for the target company.
-- 2. Create and populate the temporary table `source_accounts` from your CSV import tool.
-- 3. Replace `__COMPANY_ID__` with the target company_id and run this script.
--
-- Expected columns in `source_accounts`:
--   code VARCHAR(10)          NOT NULL
--   name VARCHAR(100)         NOT NULL
--   account_type VARCHAR(20)  NOT NULL
--   is_control_account BOOLEAN NOT NULL
--   tax_code_code VARCHAR(20) NOT NULL
--   balance_side VARCHAR(10)  NULL
--
-- Example temp table setup:
-- CREATE TEMP TABLE source_accounts (
--     code VARCHAR(10) NOT NULL,
--     name VARCHAR(100) NOT NULL,
--     account_type VARCHAR(20) NOT NULL,
--     is_control_account BOOLEAN NOT NULL,
--     tax_code_code VARCHAR(20) NOT NULL,
--     balance_side VARCHAR(10) NULL
-- ) ON COMMIT DROP;
--
-- Then load rows from CSV with your preferred tool before running the INSERTs below.

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
       COALESCE(
           NULLIF(src.balance_side, ''),
           CASE
               WHEN src.account_type IN ('asset', 'expense') THEN 'debit'
               ELSE 'credit'
           END
       ) AS balance_side,
       src.is_control_account,
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
