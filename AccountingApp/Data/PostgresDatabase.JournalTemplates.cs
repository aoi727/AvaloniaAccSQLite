using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<JournalTemplateSummary>> GetJournalTemplateSummariesAsync(int companyId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureJournalTemplateSchemaAsync(connection, null);

        const string sql = @"
    SELECT template_id, name, is_single_entry_mode, updated_at
    FROM journal_templates
    WHERE company_id = @company_id
    ORDER BY name";

        var rows = new List<JournalTemplateSummary>();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new JournalTemplateSummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetDateTime(3)));
        }

        return rows;
    }

    public async Task<JournalTemplateDetail?> GetJournalTemplateAsync(int companyId, int templateId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureJournalTemplateSchemaAsync(connection, null);

        const string templateSql = @"
    SELECT template_id, name, reference, is_single_entry_mode
    FROM journal_templates
    WHERE company_id = @company_id
      AND template_id = @template_id";

        await using var templateCommand = new SqliteCommand(templateSql, connection);
        templateCommand.Parameters.AddWithValue("company_id", companyId);
        templateCommand.Parameters.AddWithValue("template_id", templateId);

        await using var templateReader = await templateCommand.ExecuteReaderAsync();
        if (!await templateReader.ReadAsync())
        {
            return null;
        }

        var resolvedTemplateId = templateReader.GetInt32(0);
        var name = templateReader.GetString(1);
        var reference = templateReader.IsDBNull(2) ? null : templateReader.GetString(2);
        var isSingleEntryMode = templateReader.GetBoolean(3);
        await templateReader.CloseAsync();

        const string rowSql = @"
    SELECT row_no, description, partner_id, invoice_number,
           debit_account_id, debit_sub_account_id, debit_tax_code_id, debit_tax_input_type, debit_amount,
           credit_account_id, credit_sub_account_id, credit_tax_code_id, credit_tax_input_type, credit_amount
    FROM journal_template_rows
    WHERE template_id = @template_id
    ORDER BY row_no";

        var rows = new List<JournalTemplateRowData>();
        await using var rowCommand = new SqliteCommand(rowSql, connection);
        rowCommand.Parameters.AddWithValue("template_id", resolvedTemplateId);

        await using var rowReader = await rowCommand.ExecuteReaderAsync();
        while (await rowReader.ReadAsync())
        {
            rows.Add(new JournalTemplateRowData(
                rowReader.GetInt32(0),
                rowReader.IsDBNull(1) ? null : rowReader.GetString(1),
                rowReader.IsDBNull(2) ? null : rowReader.GetInt32(2),
                rowReader.IsDBNull(3) ? null : rowReader.GetString(3),
                rowReader.IsDBNull(4) ? null : rowReader.GetInt32(4),
                ReadOptionalSubAccountId(rowReader, 5),
                rowReader.IsDBNull(6) ? null : rowReader.GetInt32(6),
                rowReader.IsDBNull(7) ? "none" : rowReader.GetString(7),
                rowReader.IsDBNull(8) ? null : rowReader.GetDecimal(8),
                rowReader.IsDBNull(9) ? null : rowReader.GetInt32(9),
                ReadOptionalSubAccountId(rowReader, 10),
                rowReader.IsDBNull(11) ? null : rowReader.GetInt32(11),
                rowReader.IsDBNull(12) ? "none" : rowReader.GetString(12),
                rowReader.IsDBNull(13) ? null : rowReader.GetDecimal(13)));
        }

        return new JournalTemplateDetail(resolvedTemplateId, name, reference, isSingleEntryMode, rows);
    }

    public async Task SaveJournalTemplateAsync(
        int companyId,
        int userId,
        string name,
        string? reference,
        bool isSingleEntryMode,
        IReadOnlyList<JournalTemplateRowData> rows)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("テンプレート名を入力してください。");
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("テンプレートに保存する明細がありません。");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureJournalTemplateSchemaAsync(connection, transaction);
            await EnsureOperationLogSchemaAsync(connection, transaction);

            var normalizedName = name.Trim();
            var templateId = await FindJournalTemplateIdByNameAsync(connection, transaction, companyId, normalizedName);
            if (templateId.HasValue)
            {
                await UpdateJournalTemplateAsync(connection, transaction, templateId.Value, normalizedName, reference, isSingleEntryMode);
                await DeleteJournalTemplateRowsAsync(connection, transaction, templateId.Value);
            }
            else
            {
                templateId = await InsertJournalTemplateAsync(connection, transaction, companyId, userId, normalizedName, reference, isSingleEntryMode);
            }

            foreach (var row in rows.OrderBy(x => x.RowNo))
            {
                await InsertJournalTemplateRowAsync(connection, transaction, templateId.Value, row);
            }

            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "journal_template_save",
                "journal_template",
                normalizedName,
                $"定型仕訳を保存しました: {normalizedName}");

            await transaction.CommitAsync();
            committed = true;
        }
        catch
        {
            if (!committed)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
    }

    public async Task DeleteJournalTemplateAsync(int companyId, int userId, int templateId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureJournalTemplateSchemaAsync(connection, transaction);
            await EnsureOperationLogSchemaAsync(connection, transaction);

            var template = await GetJournalTemplateAsync(companyId, templateId);
            if (template is null)
            {
                throw new InvalidOperationException("削除対象のテンプレートが見つかりません。");
            }

            const string sql = @"
    DELETE FROM journal_templates
    WHERE company_id = @company_id
      AND template_id = @template_id";

            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("template_id", templateId);
            await command.ExecuteNonQueryAsync();

            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "journal_template_delete",
                "journal_template",
                template.Name,
                $"定型仕訳を削除しました: {template.Name}");

            await transaction.CommitAsync();
            committed = true;
        }
        catch
        {
            if (!committed)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
    }

    private static async Task EnsureJournalTemplateSchemaAsync(SqliteConnection connection, SqliteTransaction? transaction)
    {
        const string sql = @"
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
    CREATE INDEX IF NOT EXISTS idx_journal_templates_company_name ON journal_templates(company_id, name);
    CREATE INDEX IF NOT EXISTS idx_journal_template_rows_template_row_no ON journal_template_rows(template_id, row_no);";

        await using var command = transaction is null
            ? new SqliteCommand(sql, connection)
            : new SqliteCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int?> FindJournalTemplateIdByNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        string name)
    {
        const string sql = @"
    SELECT template_id
    FROM journal_templates
    WHERE company_id = @company_id
      AND name = @name";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("name", name);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private static async Task<int> InsertJournalTemplateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        int userId,
        string name,
        string? reference,
        bool isSingleEntryMode)
    {
        const string sql = @"
    INSERT INTO journal_templates (
        company_id, name, reference, is_single_entry_mode, created_by, updated_at
    )
    VALUES (
        @company_id, @name, @reference, @is_single_entry_mode, @created_by, CURRENT_TIMESTAMP
    )
    RETURNING template_id";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("reference", (object?)(string.IsNullOrWhiteSpace(reference) ? null : reference.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("is_single_entry_mode", isSingleEntryMode);
        command.Parameters.AddWithValue("created_by", userId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task UpdateJournalTemplateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int templateId,
        string name,
        string? reference,
        bool isSingleEntryMode)
    {
        const string sql = @"
    UPDATE journal_templates
    SET name = @name,
        reference = @reference,
        is_single_entry_mode = @is_single_entry_mode,
        updated_at = CURRENT_TIMESTAMP
    WHERE template_id = @template_id";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("template_id", templateId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("reference", (object?)(string.IsNullOrWhiteSpace(reference) ? null : reference.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("is_single_entry_mode", isSingleEntryMode);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteJournalTemplateRowsAsync(SqliteConnection connection, SqliteTransaction transaction, int templateId)
    {
        const string sql = @"
    DELETE FROM journal_template_rows
    WHERE template_id = @template_id";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("template_id", templateId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertJournalTemplateRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int templateId,
        JournalTemplateRowData row)
    {
        const string sql = @"
    INSERT INTO journal_template_rows (
        template_id, row_no, description, partner_id, invoice_number,
        debit_account_id, debit_sub_account_id, debit_tax_code_id, debit_tax_input_type, debit_amount,
        credit_account_id, credit_sub_account_id, credit_tax_code_id, credit_tax_input_type, credit_amount,
        updated_at
    )
    VALUES (
        @template_id, @row_no, @description, @partner_id, @invoice_number,
        @debit_account_id, @debit_sub_account_id, @debit_tax_code_id, @debit_tax_input_type, @debit_amount,
        @credit_account_id, @credit_sub_account_id, @credit_tax_code_id, @credit_tax_input_type, @credit_amount,
        CURRENT_TIMESTAMP
    )";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("template_id", templateId);
        command.Parameters.AddWithValue("row_no", row.RowNo);
        command.Parameters.AddWithValue("description", (object?)row.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("partner_id", (object?)row.PartnerId ?? DBNull.Value);
        command.Parameters.AddWithValue("invoice_number", (object?)row.InvoiceNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("debit_account_id", (object?)row.DebitAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("debit_sub_account_id", row.DebitSubAccountId.GetValueOrDefault());
        command.Parameters.AddWithValue("debit_tax_code_id", (object?)row.DebitTaxCodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("debit_tax_input_type", row.DebitTaxInputType);
        command.Parameters.AddWithValue("debit_amount", (object?)row.DebitAmount ?? DBNull.Value);
        command.Parameters.AddWithValue("credit_account_id", (object?)row.CreditAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("credit_sub_account_id", row.CreditSubAccountId.GetValueOrDefault());
        command.Parameters.AddWithValue("credit_tax_code_id", (object?)row.CreditTaxCodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("credit_tax_input_type", row.CreditTaxInputType);
        command.Parameters.AddWithValue("credit_amount", (object?)row.CreditAmount ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static int? ReadOptionalSubAccountId(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetInt32(ordinal);
        return value <= 0 ? null : value;
    }
}
