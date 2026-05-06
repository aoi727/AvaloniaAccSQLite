PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS companies (
    company_id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name                VARCHAR(100) NOT NULL,
    fiscal_year_start   DATE NOT NULL,
    closing_day         INTEGER NOT NULL DEFAULT 31 CHECK (closing_day BETWEEN 1 AND 31),
    tax_entry_method    VARCHAR(20) NOT NULL DEFAULT 'gross' CHECK (tax_entry_method IN ('gross', 'net')),
    is_tax_exempt       BOOLEAN NOT NULL DEFAULT FALSE,
    account_set_id      INTEGER REFERENCES account_sets(account_set_id),
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TRIGGER IF NOT EXISTS trg_companies_single_row
BEFORE INSERT ON companies
WHEN (SELECT COUNT(*) FROM companies) >= 1
BEGIN
    SELECT RAISE(ABORT, 'SQLite版では1つのDBにつき会社は1社のみです。');
END;

CREATE TABLE IF NOT EXISTS users (
    user_id             INTEGER PRIMARY KEY AUTOINCREMENT,
    login_id            VARCHAR(100) NOT NULL UNIQUE,
    display_name        VARCHAR(100) NOT NULL,
    password_hash       VARCHAR(200) NOT NULL,
    password_salt       VARCHAR(200) NOT NULL,
    is_active           BOOLEAN DEFAULT TRUE,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS user_companies (
    user_id             INTEGER NOT NULL REFERENCES users(user_id),
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    role                VARCHAR(20) NOT NULL,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, company_id)
);

CREATE TABLE IF NOT EXISTS accounts (
    account_id          INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    code                VARCHAR(10) NOT NULL,
    name                VARCHAR(100) NOT NULL,
    account_type        VARCHAR(20) NOT NULL,
    balance_side        VARCHAR(10) NOT NULL DEFAULT 'debit',
    is_control_account  BOOLEAN DEFAULT FALSE,
    default_tax_code_id INTEGER REFERENCES tax_codes(tax_code_id),
    is_active           BOOLEAN DEFAULT TRUE,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, code),
    CHECK (account_type IN ('asset','liability','equity','revenue','expense')),
    CHECK (balance_side IN ('debit','credit'))
);

CREATE TABLE IF NOT EXISTS sub_accounts (
    sub_account_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    account_id          INTEGER NOT NULL REFERENCES accounts(account_id),
    code                VARCHAR(20) NOT NULL,
    name                VARCHAR(200) NOT NULL,
    external_code       VARCHAR(50),
    balance             NUMERIC(15,2) DEFAULT 0,
    is_active           BOOLEAN DEFAULT TRUE,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, account_id, code)
);

CREATE TABLE IF NOT EXISTS account_sets (
    account_set_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    name                VARCHAR(100) NOT NULL,
    source_company_id   INTEGER REFERENCES companies(company_id),
    is_active           BOOLEAN DEFAULT TRUE,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(name)
);

CREATE TABLE IF NOT EXISTS account_set_accounts (
    account_set_account_id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_set_id      INTEGER NOT NULL REFERENCES account_sets(account_set_id) ON DELETE CASCADE,
    code                VARCHAR(10) NOT NULL,
    name                VARCHAR(100) NOT NULL,
    account_type        VARCHAR(20) NOT NULL,
    balance_side        VARCHAR(10) NOT NULL DEFAULT 'debit',
    is_control_account  BOOLEAN DEFAULT FALSE,
    default_tax_code    VARCHAR(20),
    display_order       INTEGER DEFAULT 0,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(account_set_id, code),
    CHECK (account_type IN ('asset','liability','equity','revenue','expense')),
    CHECK (balance_side IN ('debit','credit'))
);

CREATE TABLE IF NOT EXISTS account_set_sub_accounts (
    account_set_sub_account_id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_set_account_id INTEGER NOT NULL REFERENCES account_set_accounts(account_set_account_id) ON DELETE CASCADE,
    code                VARCHAR(20) NOT NULL,
    name                VARCHAR(200) NOT NULL,
    external_code       VARCHAR(50),
    opening_balance     NUMERIC(15,2) DEFAULT 0,
    is_active           BOOLEAN DEFAULT TRUE,
    display_order       INTEGER DEFAULT 0,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(account_set_account_id, code)
);

CREATE TABLE IF NOT EXISTS tax_codes (
    tax_code_id         INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    code                VARCHAR(20) NOT NULL,
    name                VARCHAR(100) NOT NULL,
    tax_kind            VARCHAR(20) NOT NULL,
    tax_rate            NUMERIC(5,2) NOT NULL,
    is_purchase_credit  BOOLEAN DEFAULT FALSE,
    is_taxable          BOOLEAN DEFAULT TRUE,
    requires_invoice    BOOLEAN DEFAULT FALSE,
    default_purchase_credit_rate NUMERIC(5,2) DEFAULT 0,
    is_active           BOOLEAN DEFAULT TRUE,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, code),
    CHECK (tax_kind IN ('sales','purchase','non_taxable','exempt','out_of_scope'))
);

CREATE TABLE IF NOT EXISTS business_partners (
    partner_id          INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    code                VARCHAR(30) NOT NULL,
    name                VARCHAR(200) NOT NULL,
    partner_type        VARCHAR(20) NOT NULL DEFAULT 'supplier',
    invoice_status      VARCHAR(20) NOT NULL DEFAULT 'unknown',
    registration_number VARCHAR(20),
    is_active           BOOLEAN DEFAULT TRUE,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, code),
    CHECK (partner_type IN ('customer','supplier','both','other')),
    CHECK (invoice_status IN ('qualified','exempt','unregistered','unknown'))
);

CREATE TABLE IF NOT EXISTS journal_vouchers (
    voucher_id          INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    entry_date          DATE NOT NULL,
    entry_number        VARCHAR(20) NOT NULL,
    reference           VARCHAR(100),
    created_by          INTEGER REFERENCES users(user_id),
    source_type         VARCHAR(40) NOT NULL DEFAULT 'manual' CHECK (source_type IN ('manual','annual_carry_forward')),
    source_key          VARCHAR(100),
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, entry_number)
);

CREATE TABLE IF NOT EXISTS journal_lines (
    line_id             INTEGER PRIMARY KEY AUTOINCREMENT,
    voucher_id          INTEGER NOT NULL REFERENCES journal_vouchers(voucher_id) ON DELETE CASCADE,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    line_no             INTEGER NOT NULL,
    side                VARCHAR(6) NOT NULL,
    account_id          INTEGER NOT NULL REFERENCES accounts(account_id),
    sub_account_id      INTEGER DEFAULT 0,
    amount              NUMERIC(15,2) NOT NULL CHECK (amount > 0),
    tax_code_id         INTEGER REFERENCES tax_codes(tax_code_id),
    tax_rate            NUMERIC(5,2),
    tax_amount          NUMERIC(15,2) DEFAULT 0,
    creditable_tax_amount NUMERIC(15,2) DEFAULT 0,
    non_creditable_tax_amount NUMERIC(15,2) DEFAULT 0,
    tax_input_type      VARCHAR(10) DEFAULT 'excluded',
    description         TEXT,
    partner_id          INTEGER REFERENCES business_partners(partner_id),
    invoice_number      VARCHAR(100),
    invoice_registration_number VARCHAR(20),
    invoice_status      VARCHAR(20),
    purchase_credit_rate NUMERIC(5,2),
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(voucher_id, line_no),
    CHECK (side IN ('debit','credit'))
);

CREATE TABLE IF NOT EXISTS sub_account_balances (
    balance_id          INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    sub_account_id      INTEGER NOT NULL REFERENCES sub_accounts(sub_account_id),
    fiscal_year         INTEGER NOT NULL,
    month               INTEGER NOT NULL CHECK (month BETWEEN 1 AND 12),
    balance             NUMERIC(15,2) NOT NULL,
    UNIQUE(company_id, sub_account_id, fiscal_year, month)
);

CREATE TABLE IF NOT EXISTS annual_carry_forwards (
    carry_forward_id         INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id               INTEGER NOT NULL REFERENCES companies(company_id),
    source_fiscal_year_start DATE NOT NULL,
    source_fiscal_year_end   DATE NOT NULL,
    next_fiscal_year_start   DATE NOT NULL,
    entry_number             VARCHAR(30) NOT NULL,
    equity_account_id        INTEGER NOT NULL REFERENCES accounts(account_id),
    net_income               NUMERIC(15,2) NOT NULL,
    created_by               INTEGER REFERENCES users(user_id),
    created_at               TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, next_fiscal_year_start),
    UNIQUE(company_id, entry_number)
);

CREATE TABLE IF NOT EXISTS annual_closings (
    closing_id               INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id               INTEGER NOT NULL REFERENCES companies(company_id),
    fiscal_year_start        DATE NOT NULL,
    fiscal_year_end          DATE NOT NULL,
    next_fiscal_year_start   DATE NOT NULL,
    carry_forward_entry_number VARCHAR(30),
    status                   VARCHAR(20) NOT NULL DEFAULT 'open',
    closed_by                INTEGER REFERENCES users(user_id),
    closed_at                TIMESTAMP,
    unlocked_by              INTEGER REFERENCES users(user_id),
    unlocked_at              TIMESTAMP,
    unlock_reason            TEXT,
    created_at               TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at               TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, fiscal_year_start),
    UNIQUE(company_id, next_fiscal_year_start),
    CHECK (status IN ('open','closed'))
);

CREATE TABLE IF NOT EXISTS monthly_locks (
    monthly_lock_id INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id      INTEGER NOT NULL REFERENCES companies(company_id),
    period_start    DATE NOT NULL,
    period_end      DATE NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'open',
    locked_by       INTEGER REFERENCES users(user_id),
    locked_at       TIMESTAMP,
    unlocked_by     INTEGER REFERENCES users(user_id),
    unlocked_at     TIMESTAMP,
    unlock_reason   TEXT,
    created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, period_start),
    CHECK (status IN ('open','closed'))
);

CREATE TABLE IF NOT EXISTS operation_logs (
    log_id              INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    user_id             INTEGER REFERENCES users(user_id),
    operation_type      VARCHAR(60) NOT NULL,
    target_type         VARCHAR(60) NOT NULL,
    target_key          VARCHAR(120),
    summary             TEXT NOT NULL,
    metadata_json       TEXT,
    occurred_at         TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS journal_templates (
    template_id         INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id          INTEGER NOT NULL REFERENCES companies(company_id),
    name                VARCHAR(100) NOT NULL,
    reference           VARCHAR(100),
    is_single_entry_mode BOOLEAN NOT NULL DEFAULT FALSE,
    created_by          INTEGER REFERENCES users(user_id),
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, name)
);

CREATE TABLE IF NOT EXISTS journal_template_rows (
    template_row_id         INTEGER PRIMARY KEY AUTOINCREMENT,
    template_id             INTEGER NOT NULL REFERENCES journal_templates(template_id) ON DELETE CASCADE,
    row_no                  INTEGER NOT NULL,
    description             TEXT,
    partner_id              INTEGER REFERENCES business_partners(partner_id),
    invoice_number          VARCHAR(100),
    debit_account_id        INTEGER REFERENCES accounts(account_id),
    debit_sub_account_id    INTEGER DEFAULT 0,
    debit_tax_code_id       INTEGER REFERENCES tax_codes(tax_code_id),
    debit_tax_input_type    VARCHAR(10) DEFAULT 'none',
    debit_amount            NUMERIC(15,2),
    credit_account_id       INTEGER REFERENCES accounts(account_id),
    credit_sub_account_id   INTEGER DEFAULT 0,
    credit_tax_code_id      INTEGER REFERENCES tax_codes(tax_code_id),
    credit_tax_input_type   VARCHAR(10) DEFAULT 'none',
    credit_amount           NUMERIC(15,2),
    created_at              TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at              TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(template_id, row_no)
);

CREATE INDEX IF NOT EXISTS idx_user_companies_company ON user_companies(company_id);
CREATE INDEX IF NOT EXISTS idx_accounts_company ON accounts(company_id);
CREATE INDEX IF NOT EXISTS idx_sub_accounts_company_account ON sub_accounts(company_id, account_id);
CREATE INDEX IF NOT EXISTS idx_account_sets_source_company ON account_sets(source_company_id);
CREATE INDEX IF NOT EXISTS idx_account_set_accounts_set ON account_set_accounts(account_set_id, display_order, code);
CREATE INDEX IF NOT EXISTS idx_account_set_sub_accounts_parent ON account_set_sub_accounts(account_set_account_id, display_order, code);
CREATE INDEX IF NOT EXISTS idx_tax_codes_company ON tax_codes(company_id);
CREATE INDEX IF NOT EXISTS idx_business_partners_company ON business_partners(company_id);
CREATE INDEX IF NOT EXISTS idx_journal_vouchers_company_date ON journal_vouchers(company_id, entry_date);
CREATE INDEX IF NOT EXISTS idx_journal_vouchers_company_number ON journal_vouchers(company_id, entry_number);
CREATE INDEX IF NOT EXISTS idx_journal_lines_voucher ON journal_lines(voucher_id, line_no);
CREATE INDEX IF NOT EXISTS idx_journal_lines_company_account ON journal_lines(company_id, account_id, sub_account_id);
CREATE INDEX IF NOT EXISTS idx_journal_lines_company_partner ON journal_lines(company_id, partner_id);
CREATE INDEX IF NOT EXISTS idx_accounts_company_active ON accounts(company_id, is_active, code);
CREATE INDEX IF NOT EXISTS idx_sub_account_balances_company_period ON sub_account_balances(company_id, fiscal_year, month);
CREATE INDEX IF NOT EXISTS idx_annual_carry_forwards_company_start ON annual_carry_forwards(company_id, next_fiscal_year_start);
CREATE INDEX IF NOT EXISTS idx_annual_closings_company_year ON annual_closings(company_id, fiscal_year_start);
CREATE INDEX IF NOT EXISTS idx_annual_closings_company_status ON annual_closings(company_id, status);
CREATE INDEX IF NOT EXISTS idx_monthly_locks_company_period ON monthly_locks(company_id, period_start, period_end);
CREATE INDEX IF NOT EXISTS idx_monthly_locks_company_status ON monthly_locks(company_id, status);
CREATE INDEX IF NOT EXISTS idx_journal_vouchers_company_source ON journal_vouchers(company_id, source_type, source_key);
CREATE INDEX IF NOT EXISTS idx_operation_logs_company_time ON operation_logs(company_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_operation_logs_company_target ON operation_logs(company_id, target_type, target_key);
CREATE INDEX IF NOT EXISTS idx_journal_templates_company_name ON journal_templates(company_id, name);
CREATE INDEX IF NOT EXISTS idx_journal_template_rows_template_row_no ON journal_template_rows(template_id, row_no);
