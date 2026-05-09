using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<string> GetNextEntryNumberAsync(int companyId, DateTime entryDate)
    {
        var prefix = $"J{entryDate:yyyyMMdd}-";
        const string sql = @"
    SELECT MAX(entry_number)
    FROM journal_vouchers
    WHERE company_id = @company_id
      AND entry_number LIKE @prefix";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("prefix", prefix + "%");
        var result = await command.ExecuteScalarAsync();
        var maxNumber = result == DBNull.Value ? null : Convert.ToString(result);

        var next = 1;
        if (!string.IsNullOrWhiteSpace(maxNumber) &&
            TryExtractEntryNumberSequence(maxNumber, prefix, out var current))
        {
            next = current + 1;
        }

        return prefix + next.ToString("0000");
    }

    public async Task SaveJournalVoucherAsync(
            int companyId,
            string entryNumber,
            DateTime entryDate,
            string? reference,
            int createdBy,
            IReadOnlyList<JournalLineInput> lines,
            IReadOnlyList<JournalVoucherAttachment>? attachments = null,
            string? originalEntryNumber = null)
    {
        ValidateEntryNumber(entryNumber);
        ValidateVoucherLines(lines);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureJournalAttachmentSchemaAsync(connection, transaction);
            var normalizedEntryNumber = entryNumber.Trim();
            var normalizedOriginalEntryNumber = string.IsNullOrWhiteSpace(originalEntryNumber)
                ? normalizedEntryNumber
                : originalEntryNumber.Trim();
            var isRenamingExistingVoucher = !string.Equals(normalizedOriginalEntryNumber, normalizedEntryNumber, StringComparison.Ordinal);

            var existingDate = await GetJournalVoucherDateAsync(connection, transaction, companyId, normalizedOriginalEntryNumber);
            if (originalEntryNumber is not null && !existingDate.HasValue)
            {
                throw new InvalidOperationException("更新対象の仕訳が見つかりませんでした。");
            }

            await EnsureJournalDateOpenAsync(connection, transaction, companyId, entryDate);
            if (existingDate.HasValue)
            {
                await EnsureJournalDateOpenAsync(connection, transaction, companyId, existingDate.Value);
            }

            if (isRenamingExistingVoucher)
            {
                var destinationExistingDate = await GetJournalVoucherDateAsync(connection, transaction, companyId, normalizedEntryNumber);
                if (destinationExistingDate.HasValue)
                {
                    throw new InvalidOperationException($"変更後の伝票番号は既に使用されています: {normalizedEntryNumber}");
                }

                await DeleteJournalVoucherAsync(connection, transaction, companyId, normalizedOriginalEntryNumber);
            }
            else
            {
                await EnsureJournalVoucherEditableAsync(connection, transaction, companyId, normalizedEntryNumber, entryDate);
                await DeleteJournalVoucherAsync(connection, transaction, companyId, normalizedEntryNumber);
            }

            var voucherId = await InsertJournalVoucherAsync(
                connection,
                transaction,
                companyId,
                normalizedEntryNumber,
                entryDate,
                reference,
                createdBy);

            var lineNo = 1;
            foreach (var line in lines)
            {
                await InsertJournalLineAsync(
                    connection,
                    transaction,
                    voucherId,
                    companyId,
                    lineNo++,
                    line);
            }

            if (attachments is not null)
            {
                foreach (var attachment in attachments)
                {
                    await InsertJournalAttachmentAsync(connection, transaction, voucherId, companyId, attachment);
                }
            }

            await EnsureOperationLogSchemaAsync(connection, transaction);
            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                createdBy,
                existingDate.HasValue ? "journal_update" : "journal_create",
                "journal",
                normalizedEntryNumber,
                existingDate.HasValue ? $"仕訳を更新しました: {entryNumber}" : $"仕訳を登録しました: {entryNumber}");

            await transaction.CommitAsync();
            committed = true;
            await RebuildSubAccountBalancesAsync(companyId);
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

    public async Task SaveJournalVouchersAsync(
            int companyId,
            string baseEntryNumber,
            DateTime entryDate,
            string? reference,
            int createdBy,
            IReadOnlyList<IReadOnlyList<JournalLineInput>> vouchers)
    {
        ValidateEntryNumber(baseEntryNumber);
        if (vouchers.Count == 0)
        {
            throw new InvalidOperationException("保存する仕訳がありません。");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureOperationLogSchemaAsync(connection, transaction);

            var currentEntryNumber = baseEntryNumber.Trim();
            foreach (var voucherLines in vouchers)
            {
                ValidateVoucherLines(voucherLines);
                await EnsureJournalVoucherEditableAsync(connection, transaction, companyId, currentEntryNumber, entryDate);

                var voucherId = await InsertJournalVoucherAsync(
                    connection,
                    transaction,
                    companyId,
                    currentEntryNumber,
                    entryDate,
                    reference,
                    createdBy);

                var lineNo = 1;
                foreach (var line in voucherLines)
                {
                    await InsertJournalLineAsync(
                        connection,
                        transaction,
                        voucherId,
                        companyId,
                        lineNo++,
                        line);
                }

                await WriteOperationLogAsync(
                    connection,
                    transaction,
                    companyId,
                    createdBy,
                    "journal_create",
                    "journal",
                    currentEntryNumber,
                    $"仕訳を登録しました: {currentEntryNumber}");

                currentEntryNumber = IncrementEntryNumber(currentEntryNumber);
            }

            await transaction.CommitAsync();
            committed = true;
            await RebuildSubAccountBalancesAsync(companyId);
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

    public async Task DeleteJournalVoucherAsync(int companyId, int userId, string entryNumber)
    {
        if (string.IsNullOrWhiteSpace(entryNumber))
        {
            throw new InvalidOperationException("伝票番号を指定してください。");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            var existingDate = await GetJournalVoucherDateAsync(connection, transaction, companyId, entryNumber);
            if (!existingDate.HasValue)
            {
                throw new InvalidOperationException("指定した仕訳が見つかりませんでした。");
            }

            await EnsureJournalDateOpenAsync(connection, transaction, companyId, existingDate.Value);
            await DeleteAnnualCarryForwardExecutionAsync(connection, transaction, companyId, entryNumber);
            var deletedCount = await DeleteJournalVoucherAsync(connection, transaction, companyId, entryNumber);
            if (deletedCount == 0)
            {
                throw new InvalidOperationException("指定した仕訳が見つかりませんでした。");
            }

            await EnsureOperationLogSchemaAsync(connection, transaction);
            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "journal_delete",
                "journal",
                entryNumber,
                $"仕訳を削除しました: {entryNumber}");

            await transaction.CommitAsync();
            committed = true;
            await RebuildSubAccountBalancesAsync(companyId);
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

    public async Task<IReadOnlyList<JournalVoucherAttachment>> GetJournalVoucherAttachmentsAsync(int companyId, string entryNumber)
    {
        const string sql = @"
    SELECT a.attachment_id,
           a.file_name,
           a.content_type,
           a.content
    FROM journal_voucher_attachments a
    JOIN journal_vouchers v ON v.voucher_id = a.voucher_id
    WHERE v.company_id = @company_id
      AND v.entry_number = @entry_number
    ORDER BY a.attachment_id";

        var attachments = new List<JournalVoucherAttachment>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureJournalAttachmentSchemaAsync(connection, null);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("entry_number", entryNumber.Trim());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            attachments.Add(new JournalVoucherAttachment(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                (byte[])reader["content"]));
        }

        return attachments;
    }

    public async Task<IReadOnlyList<JournalVoucherSummary>> GetJournalVoucherSummariesAsync(int companyId)
    {
        return await GetJournalVoucherSummariesAsync(companyId, null, null);
    }

    public async Task<IReadOnlyList<JournalBookRow>> GetJournalBookRowsAsync(int companyId, DateTime? fromDate, DateTime? toDate)
    {
        const string sql = @"
    SELECT l.line_id,
           v.entry_date,
           v.entry_number,
           l.description,
           v.reference,
           p.name AS partner_name,
           CASE
               WHEN l.side = 'debit' THEN
                   a.code || ' ' || a.name ||
                   CASE
                       WHEN s.sub_account_id IS NOT NULL AND s.code <> '0' THEN ' / ' || s.code || ' ' || s.name
                       ELSE ''
                   END
               ELSE NULL
           END AS debit_account_display,
           CASE
               WHEN l.side = 'credit' THEN
                   a.code || ' ' || a.name ||
                   CASE
                       WHEN s.sub_account_id IS NOT NULL AND s.code <> '0' THEN ' / ' || s.code || ' ' || s.name
                       ELSE ''
                   END
               ELSE NULL
           END AS credit_account_display,
           CASE WHEN l.side = 'debit' THEN l.amount ELSE 0 END AS debit_amount,
           CASE WHEN l.side = 'credit' THEN l.amount ELSE 0 END AS credit_amount
    FROM journal_vouchers v
    JOIN journal_lines l ON l.voucher_id = v.voucher_id
    JOIN accounts a ON a.account_id = l.account_id
    LEFT JOIN sub_accounts s ON s.sub_account_id = NULLIF(l.sub_account_id, 0)
    LEFT JOIN business_partners p ON p.partner_id = l.partner_id
    WHERE v.company_id = @company_id
      AND (@from_date IS NULL OR v.entry_date >= @from_date)
      AND (@to_date IS NULL OR v.entry_date < @to_date)
    ORDER BY v.entry_date, v.entry_number, l.line_no";

        var rows = new List<JournalBookRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", (object?)(fromDate?.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("to_date", (object?)(toDate?.Date) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new JournalBookRow(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<JournalVoucherSummary>> GetJournalVoucherSummariesAsync(int companyId, DateTime? fromDate, DateTime? toDate)
    {
        const string sql = @"
    SELECT v.entry_number,
           v.entry_date,
           MIN(l.description) AS description,
           v.reference,
           (
               SELECT group_concat(name, ' / ')
               FROM (
                   SELECT DISTINCT da.name AS name
                   FROM journal_lines dl
                   JOIN accounts da ON da.account_id = dl.account_id
                   WHERE dl.voucher_id = v.voucher_id
                     AND dl.side = 'debit'
                   ORDER BY da.name
               )
           ) AS debit_accounts,
           (
               SELECT group_concat(name, ' / ')
               FROM (
                   SELECT DISTINCT ca.name AS name
                   FROM journal_lines cl
                   JOIN accounts ca ON ca.account_id = cl.account_id
                   WHERE cl.voucher_id = v.voucher_id
                     AND cl.side = 'credit'
                   ORDER BY ca.name
               )
           ) AS credit_accounts,
           COALESCE(SUM(CASE WHEN l.side = 'debit' THEN l.amount ELSE 0 END), 0) AS debit_total,
           COALESCE(SUM(CASE WHEN l.side = 'credit' THEN l.amount ELSE 0 END), 0) AS credit_total,
           COUNT(*) AS line_count
    FROM journal_vouchers v
    JOIN journal_lines l ON l.voucher_id = v.voucher_id
    JOIN accounts a ON a.account_id = l.account_id
    WHERE v.company_id = @company_id
      AND (@from_date IS NULL OR v.entry_date >= @from_date)
      AND (@to_date IS NULL OR v.entry_date < @to_date)
    GROUP BY v.voucher_id, v.entry_number, v.entry_date, v.reference
    ORDER BY v.entry_date, v.entry_number";

        var vouchers = new List<JournalVoucherSummary>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", (object?)(fromDate?.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("to_date", (object?)(toDate?.Date) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            vouchers.Add(new JournalVoucherSummary(
                reader.GetString(0),
                reader.GetDateTime(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                Convert.ToInt32(reader.GetInt64(8))));
        }

        return vouchers;
    }

    public async Task<IReadOnlyList<JournalLine>> GetJournalLinesAsync(int companyId, string entryNumber)
    {
        const string sql = @"
    SELECT l.line_id,
           l.side,
           l.account_id,
           a.code AS account_code,
           a.name AS account_name,
           NULLIF(l.sub_account_id, 0) AS sub_account_id,
           s.code AS sub_account_code,
           s.name AS sub_account_name,
           l.amount,
           l.tax_code_id,
           l.tax_rate,
           COALESCE(l.tax_amount, 0) AS tax_amount,
           COALESCE(l.creditable_tax_amount, 0) AS creditable_tax_amount,
           COALESCE(l.non_creditable_tax_amount, 0) AS non_creditable_tax_amount,
           COALESCE(l.tax_input_type, 'excluded') AS tax_input_type,
           l.description,
           l.partner_id,
           p.code AS partner_code,
           p.name AS partner_name,
           l.invoice_number,
           l.invoice_registration_number,
           l.invoice_status,
           l.purchase_credit_rate
    FROM journal_vouchers v
    JOIN journal_lines l ON l.voucher_id = v.voucher_id
    JOIN accounts a ON a.account_id = l.account_id
    LEFT JOIN sub_accounts s ON s.sub_account_id = l.sub_account_id
    LEFT JOIN business_partners p ON p.partner_id = l.partner_id
    WHERE v.company_id = @company_id
      AND v.entry_number = @entry_number
    ORDER BY l.line_no";

        var lines = new List<JournalLine>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("entry_number", entryNumber);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(new JournalLine(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetDecimal(12),
                reader.GetDecimal(13),
                reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetInt32(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetDecimal(22)));
        }

        return lines;
    }

    public async Task<IReadOnlyList<JournalCsvRow>> GetJournalCsvRowsAsync(int companyId, DateTime? fromDate, DateTime? toDate)
    {
        const string sql = @"
    SELECT v.entry_date,
           v.entry_number,
           v.reference,
           l.side,
           a.code AS account_code,
           CASE
               WHEN l.sub_account_id IS NULL OR l.sub_account_id = 0 THEN NULL
               ELSE s.code
           END AS sub_account_code,
           l.amount,
           tc.code AS tax_code,
           l.tax_rate,
           COALESCE(l.tax_amount, 0) AS tax_amount,
           COALESCE(l.creditable_tax_amount, 0) AS creditable_tax_amount,
           COALESCE(l.non_creditable_tax_amount, 0) AS non_creditable_tax_amount,
           COALESCE(l.tax_input_type, 'none') AS tax_input_type,
           p.code AS partner_code,
           l.invoice_number,
           l.invoice_registration_number,
           l.invoice_status,
           l.purchase_credit_rate,
           l.description
    FROM journal_vouchers v
    JOIN journal_lines l ON l.voucher_id = v.voucher_id
    JOIN accounts a ON a.account_id = l.account_id
    LEFT JOIN sub_accounts s ON s.sub_account_id = NULLIF(l.sub_account_id, 0)
    LEFT JOIN tax_codes tc ON tc.tax_code_id = l.tax_code_id
    LEFT JOIN business_partners p ON p.partner_id = l.partner_id
    WHERE v.company_id = @company_id
      AND (@from_date IS NULL OR v.entry_date >= @from_date)
      AND (@to_date IS NULL OR v.entry_date < @to_date)
    ORDER BY v.entry_date, v.entry_number, l.line_no";

        var rows = new List<JournalCsvRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", (object?)(fromDate?.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("to_date", (object?)(toDate?.Date) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new JournalCsvRow(
                reader.GetDateTime(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                reader.IsDBNull(18) ? null : reader.GetString(18)));
        }

        return rows;
    }

    public async Task ImportJournalCsvAsync(int companyId, int createdBy, IReadOnlyList<JournalCsvRow> rows, DateTime? expectedMonth = null)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("インポートするCSV行がありません。");
        }

        if (expectedMonth.HasValue)
        {
            var monthStart = new DateTime(expectedMonth.Value.Year, expectedMonth.Value.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            if (rows.Any(x => x.EntryDate < monthStart || x.EntryDate >= monthEnd))
            {
                throw new InvalidOperationException("CSV内に現在表示中の月以外の仕訳日付が含まれています。");
            }
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            var accountMap = await LoadAccountCodeMapAsync(connection, transaction, companyId);
            var subAccountMap = await LoadSubAccountCodeMapAsync(connection, transaction, companyId);
            var taxCodeMap = await LoadTaxCodeMapAsync(connection, transaction, companyId);
            var partnerCodeMap = await LoadPartnerCodeMapAsync(connection, transaction, companyId);

            await EnsureOperationLogSchemaAsync(connection, transaction);

            foreach (var batch in BuildImportBatches(rows))
            {
                var first = batch.Rows[0];
                if (batch.Rows.Any(x => x.EntryDate != first.EntryDate))
                {
                    throw new InvalidOperationException($"伝票番号 {first.EntryNumber ?? "(自動採番)"} に複数の日付が混在しています。");
                }

                var reference = first.Reference;
                if (batch.Rows.Any(x => !string.Equals(x.Reference ?? string.Empty, reference ?? string.Empty, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException($"伝票番号 {first.EntryNumber ?? "(自動採番)"} に複数の参照番号が混在しています。");
                }

                var entryNumber = string.IsNullOrWhiteSpace(batch.EntryNumber)
                    ? await GetNextEntryNumberAsync(connection, transaction, companyId, first.EntryDate)
                    : batch.EntryNumber;

                var lineInputs = batch.Rows
                    .Select(x => BuildJournalLineInputFromCsvRow(x, accountMap, subAccountMap, taxCodeMap, partnerCodeMap))
                    .ToList();

                ValidateVoucherLines(lineInputs);

                var existingDate = await GetJournalVoucherDateAsync(connection, transaction, companyId, entryNumber);
                await EnsureJournalVoucherEditableAsync(connection, transaction, companyId, entryNumber, first.EntryDate);
                await DeleteAnnualCarryForwardExecutionAsync(connection, transaction, companyId, entryNumber);
                await DeleteJournalVoucherAsync(connection, transaction, companyId, entryNumber);

                var voucherId = await InsertJournalVoucherAsync(
                    connection,
                    transaction,
                    companyId,
                    entryNumber,
                    first.EntryDate,
                    reference,
                    createdBy);

                var lineNo = 1;
                foreach (var line in lineInputs)
                {
                    await InsertJournalLineAsync(connection, transaction, voucherId, companyId, lineNo++, line);
                }

                await WriteOperationLogAsync(
                    connection,
                    transaction,
                    companyId,
                    createdBy,
                    existingDate.HasValue ? "journal_update" : "journal_create",
                    "journal",
                    entryNumber,
                    existingDate.HasValue
                        ? $"CSVインポートで仕訳を更新しました: {entryNumber}"
                        : $"CSVインポートで仕訳を作成しました: {entryNumber}");
            }

            await transaction.CommitAsync();
            committed = true;
            await RebuildSubAccountBalancesAsync(companyId);
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

    public async Task<IReadOnlyList<CashbookLine>> GetCashbookLinesAsync(int companyId, int accountId, int? subAccountId, DateTime fromDate, DateTime toDate)
    {
        const string sql = @"
    WITH target_lines AS (
        SELECT l.line_id,
               l.voucher_id,
               l.line_no,
               v.entry_date,
               v.entry_number,
               l.description,
               v.reference,
               p.code AS partner_code,
               p.name AS partner_name,
               l.invoice_number,
               l.side,
               l.amount
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        LEFT JOIN business_partners p ON p.partner_id = l.partner_id
        WHERE l.company_id = @company_id
          AND l.account_id = @account_id
          AND (@sub_account_id IS NULL OR l.sub_account_id = @sub_account_id)
          AND v.entry_date >= @from_date
          AND v.entry_date <= @to_date
    ),
    counterpart_summary AS (
        SELECT cl.voucher_id,
               cl.side,
               COUNT(*) AS counterpart_count,
               MIN(a.code) AS account_code,
               MIN(a.name) AS account_name,
               MIN(s.code) AS sub_account_code,
               MIN(s.name) AS sub_account_name
        FROM journal_lines cl
        JOIN accounts a ON a.account_id = cl.account_id
        LEFT JOIN sub_accounts s ON s.sub_account_id = cl.sub_account_id
        GROUP BY cl.voucher_id, cl.side
    )
    SELECT t.line_id,
           t.entry_date,
           t.entry_number,
           t.description,
           t.reference,
           CASE WHEN t.side = 'debit' THEN t.amount ELSE 0 END AS receipt,
           CASE WHEN t.side = 'credit' THEN t.amount ELSE 0 END AS payment,
           CASE
               WHEN cp.counterpart_count = 1 THEN cp.account_code
               WHEN cp.counterpart_count > 1 THEN '諸口'
               ELSE NULL
           END AS counterpart_account_code,
           CASE
               WHEN cp.counterpart_count = 1 THEN cp.account_name
               WHEN cp.counterpart_count > 1 THEN '複合仕訳'
               ELSE NULL
           END AS counterpart_account_name,
           CASE WHEN cp.counterpart_count = 1 THEN cp.sub_account_code ELSE NULL END AS counterpart_sub_account_code,
           CASE WHEN cp.counterpart_count = 1 THEN cp.sub_account_name ELSE NULL END AS counterpart_sub_account_name,
           t.partner_code,
           t.partner_name,
           t.invoice_number
    FROM target_lines t
    LEFT JOIN counterpart_summary cp
      ON cp.voucher_id = t.voucher_id
     AND cp.side <> t.side
    ORDER BY t.entry_date, t.entry_number, t.line_no, t.line_id";

        var lines = new List<CashbookLine>();
        var balance = await GetOpeningBalanceAsync(companyId, accountId, subAccountId, fromDate);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("sub_account_id", (object?)(subAccountId) ?? DBNull.Value);
        command.Parameters.AddWithValue("from_date", fromDate.Date);
        command.Parameters.AddWithValue("to_date", toDate.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var receipt = reader.GetDecimal(5);
            var payment = reader.GetDecimal(6);
            balance += receipt - payment;

            lines.Add(new CashbookLine(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                receipt,
                payment,
                balance));
        }

        return lines;
    }

    public async Task<decimal> GetCashbookOpeningBalanceAsync(int companyId, int accountId, int? subAccountId, DateTime fromDate)
    {
        return await GetOpeningBalanceAsync(companyId, accountId, subAccountId, fromDate);
    }

    public async Task<IReadOnlyList<GeneralLedgerLine>> GetGeneralLedgerLinesAsync(int companyId, int accountId, int? subAccountId, DateTime fromDate, DateTime toDate)
    {
        const string sql = @"
    WITH account_info AS (
        SELECT balance_side
        FROM accounts
        WHERE company_id = @company_id
          AND account_id = @account_id
    ),
    target_lines AS (
        SELECT l.line_id,
               l.voucher_id,
               l.line_no,
               v.entry_date,
               v.entry_number,
               l.description,
               v.reference,
               p.code AS partner_code,
               p.name AS partner_name,
               l.invoice_number,
               l.side,
               l.amount,
               ai.balance_side
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        JOIN account_info ai ON TRUE
        LEFT JOIN business_partners p ON p.partner_id = l.partner_id
        WHERE l.company_id = @company_id
          AND l.account_id = @account_id
          AND (@sub_account_id IS NULL OR l.sub_account_id = @sub_account_id)
          AND v.entry_date >= @from_date
          AND v.entry_date <= @to_date
    ),
    counterpart_summary AS (
        SELECT cl.voucher_id,
               cl.side,
               COUNT(*) AS counterpart_count,
               MIN(a.code) AS account_code,
               MIN(a.name) AS account_name,
               MIN(s.code) AS sub_account_code,
               MIN(s.name) AS sub_account_name
        FROM journal_lines cl
        JOIN accounts a ON a.account_id = cl.account_id
        LEFT JOIN sub_accounts s ON s.sub_account_id = cl.sub_account_id
        GROUP BY cl.voucher_id, cl.side
    )
    SELECT t.line_id,
           t.entry_date,
           t.entry_number,
           t.description,
           t.reference,
           CASE
               WHEN cp.counterpart_count = 1 THEN cp.account_code
               WHEN cp.counterpart_count > 1 THEN '諸口'
               ELSE NULL
           END AS counterpart_account_code,
           CASE
               WHEN cp.counterpart_count = 1 THEN cp.account_name
               WHEN cp.counterpart_count > 1 THEN '複合仕訳'
               ELSE NULL
           END AS counterpart_account_name,
           CASE WHEN cp.counterpart_count = 1 THEN cp.sub_account_code ELSE NULL END AS counterpart_sub_account_code,
           CASE WHEN cp.counterpart_count = 1 THEN cp.sub_account_name ELSE NULL END AS counterpart_sub_account_name,
           t.partner_code,
           t.partner_name,
           t.invoice_number,
           CASE WHEN t.side = 'debit' THEN t.amount ELSE 0 END AS debit_amount,
           CASE WHEN t.side = 'credit' THEN t.amount ELSE 0 END AS credit_amount,
           CASE
               WHEN t.balance_side = 'debit' AND t.side = 'debit' THEN t.amount
               WHEN t.balance_side = 'debit' AND t.side = 'credit' THEN -t.amount
               WHEN t.balance_side = 'credit' AND t.side = 'credit' THEN t.amount
               ELSE -t.amount
           END AS balance_change
    FROM target_lines t
    LEFT JOIN counterpart_summary cp
      ON cp.voucher_id = t.voucher_id
     AND cp.side <> t.side
    ORDER BY t.entry_date, t.entry_number, t.line_no, t.line_id";

        var lines = new List<GeneralLedgerLine>();
        var balance = await GetOpeningBalanceAsync(companyId, accountId, subAccountId, fromDate);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("sub_account_id", (object?)(subAccountId) ?? DBNull.Value);
        command.Parameters.AddWithValue("from_date", fromDate.Date);
        command.Parameters.AddWithValue("to_date", toDate.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            balance += reader.GetDecimal(14);

            lines.Add(new GeneralLedgerLine(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetDecimal(12),
                reader.GetDecimal(13),
                balance));
        }

        return lines;
    }

    public async Task<decimal> GetGeneralLedgerOpeningBalanceAsync(int companyId, int accountId, int? subAccountId, DateTime fromDate)
    {
        return await GetOpeningBalanceAsync(companyId, accountId, subAccountId, fromDate);
    }

    private async Task<decimal> GetOpeningBalanceAsync(int companyId, int accountId, int? subAccountId)
    {
        return await GetOpeningBalanceAsync(companyId, accountId, subAccountId, null);
    }

    private async Task<decimal> GetOpeningBalanceAsync(int companyId, int accountId, int? subAccountId, DateTime? beforeDate)
    {
        var openingBalance = await GetConfiguredOpeningBalanceAsync(companyId, accountId, subAccountId);
        if (!beforeDate.HasValue)
        {
            return openingBalance;
        }

        const string sql = @"
    WITH account_info AS (
        SELECT balance_side
        FROM accounts
        WHERE company_id = @company_id
          AND account_id = @account_id
    )
    SELECT COALESCE(SUM(
        CASE
            WHEN ai.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
            WHEN ai.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
            WHEN ai.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
            ELSE -l.amount
        END), 0)
    FROM journal_lines l
    JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
    JOIN account_info ai ON TRUE
    WHERE l.company_id = @company_id
      AND l.account_id = @account_id
      AND (@sub_account_id IS NULL OR l.sub_account_id = @sub_account_id)
      AND v.entry_date < @before_date";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("sub_account_id", (object?)(subAccountId) ?? DBNull.Value);
        command.Parameters.AddWithValue("before_date", beforeDate.Value.Date);
        var result = await command.ExecuteScalarAsync();
        var movement = result == null || result == DBNull.Value ? 0 : Convert.ToDecimal(result);
        return openingBalance + movement;
    }

    private async Task<decimal> GetConfiguredOpeningBalanceAsync(int companyId, int accountId, int? subAccountId)
    {
        if (!subAccountId.HasValue)
        {
            const string accountSql = @"
    SELECT COALESCE(SUM(balance), 0)
    FROM sub_accounts
    WHERE company_id = @company_id
      AND account_id = @account_id
      AND is_active = TRUE";

            await using var accountConnection = new SqliteConnection(_connectionString);
            await accountConnection.OpenAsync();
            await using var accountCommand = new SqliteCommand(accountSql, accountConnection);
            accountCommand.Parameters.AddWithValue("company_id", companyId);
            accountCommand.Parameters.AddWithValue("account_id", accountId);
            var accountResult = await accountCommand.ExecuteScalarAsync();
            return accountResult == null || accountResult == DBNull.Value ? 0 : Convert.ToDecimal(accountResult);
        }

        const string sql = @"
    SELECT COALESCE(balance, 0)
    FROM sub_accounts
    WHERE company_id = @company_id
      AND account_id = @account_id
      AND sub_account_id = @sub_account_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("sub_account_id", subAccountId.Value);
        var result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToDecimal(result);
    }

    private static async Task<long> InsertJournalVoucherAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int companyId,
            string entryNumber,
            DateTime entryDate,
            string? reference,
            int createdBy,
            string sourceType = "manual",
            string? sourceKey = null)
    {
        const string sql = @"
    INSERT INTO journal_vouchers (
        company_id, entry_date, entry_number, reference, created_by, source_type, source_key, updated_at
    )
    VALUES (
        @company_id, @entry_date, @entry_number, @reference, @created_by, @source_type, @source_key, CURRENT_TIMESTAMP
    )
    RETURNING voucher_id";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("entry_date", entryDate.Date);
        command.Parameters.AddWithValue("entry_number", entryNumber.Trim());
        command.Parameters.AddWithValue("reference", (object?)(string.IsNullOrWhiteSpace(reference) ? null : reference.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by", createdBy);
        command.Parameters.AddWithValue("source_type", sourceType);
        command.Parameters.AddWithValue("source_key", (object?)(sourceKey) ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static JournalLineInput BuildJournalLineInputFromCsvRow(
        JournalCsvRow row,
        IReadOnlyDictionary<string, int> accountMap,
        IReadOnlyDictionary<string, int> subAccountMap,
        IReadOnlyDictionary<string, int> taxCodeMap,
        IReadOnlyDictionary<string, int> partnerCodeMap)
    {
        if (!accountMap.TryGetValue(row.AccountCode, out var accountId))
        {
            throw new InvalidOperationException($"勘定科目コードが見つかりません: {row.AccountCode}");
        }

        int? subAccountId = null;
        if (!string.IsNullOrWhiteSpace(row.SubAccountCode))
        {
            var subAccountKey = BuildSubAccountKey(accountId, row.SubAccountCode);
            if (!subAccountMap.TryGetValue(subAccountKey, out var resolvedSubAccountId))
            {
                throw new InvalidOperationException($"補助科目コードが見つかりません: {row.AccountCode} / {row.SubAccountCode}");
            }

            subAccountId = resolvedSubAccountId;
        }

        int? taxCodeId = null;
        if (!string.IsNullOrWhiteSpace(row.TaxCode))
        {
            if (!taxCodeMap.TryGetValue(row.TaxCode, out var resolvedTaxCodeId))
            {
                throw new InvalidOperationException($"税区分コードが見つかりません: {row.TaxCode}");
            }

            taxCodeId = resolvedTaxCodeId;
        }

        int? partnerId = null;
        if (!string.IsNullOrWhiteSpace(row.PartnerCode))
        {
            if (!partnerCodeMap.TryGetValue(row.PartnerCode, out var resolvedPartnerId))
            {
                throw new InvalidOperationException($"取引先コードが見つかりません: {row.PartnerCode}");
            }

            partnerId = resolvedPartnerId;
        }

        return new JournalLineInput(
            row.Side,
            accountId,
            subAccountId,
            row.Amount,
            taxCodeId,
            row.TaxRate,
            row.TaxAmount,
            row.CreditableTaxAmount,
            row.NonCreditableTaxAmount,
            string.IsNullOrWhiteSpace(row.TaxInputType) ? "none" : row.TaxInputType,
            row.Description,
            partnerId,
            row.InvoiceNumber,
            row.InvoiceRegistrationNumber,
            row.InvoiceStatus,
            row.PurchaseCreditRate);
    }

    private static List<ImportVoucherBatch> BuildImportBatches(IReadOnlyList<JournalCsvRow> rows)
    {
        var batches = new List<ImportVoucherBatch>();
        var blankBuffer = new List<JournalCsvRow>();

        void FlushBlankBuffer()
        {
            if (blankBuffer.Count == 0)
            {
                return;
            }

            var first = blankBuffer[0];
            if (blankBuffer.Count != 2 ||
                blankBuffer.Count(x => x.Side == "debit") != 1 ||
                blankBuffer.Count(x => x.Side == "credit") != 1)
            {
                throw new InvalidOperationException("伝票番号が空欄のCSVは単一仕訳のみ取り込めます。複合仕訳は伝票番号を指定してください。");
            }

            batches.Add(new ImportVoucherBatch(null, blankBuffer.ToList()));
            blankBuffer.Clear();
        }

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.EntryNumber))
            {
                FlushBlankBuffer();
                batches.Add(new ImportVoucherBatch(row.EntryNumber.Trim(), [row]));
                continue;
            }

            if (blankBuffer.Count == 0)
            {
                blankBuffer.Add(row);
                continue;
            }

            var first = blankBuffer[0];
            var sameBatch =
                row.EntryDate == first.EntryDate &&
                string.Equals(row.Reference ?? string.Empty, first.Reference ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(row.Description ?? string.Empty, first.Description ?? string.Empty, StringComparison.Ordinal);

            if (!sameBatch)
            {
                FlushBlankBuffer();
            }

            blankBuffer.Add(row);
        }

        FlushBlankBuffer();

        return batches
            .GroupBy(x => x.EntryNumber, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    return group;
                }

                var combinedRows = group.SelectMany(x => x.Rows).ToList();
                return [new ImportVoucherBatch(group.Key, combinedRows)];
            })
            .ToList();
    }

    private static async Task<Dictionary<string, int>> LoadAccountCodeMapAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId)
    {
        const string sql = @"
    SELECT code, account_id
    FROM accounts
    WHERE company_id = @company_id";

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] = reader.GetInt32(1);
        }

        return map;
    }

    private static async Task<Dictionary<string, int>> LoadSubAccountCodeMapAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId)
    {
        const string sql = @"
    SELECT account_id, code, sub_account_id
    FROM sub_accounts
    WHERE company_id = @company_id";

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[BuildSubAccountKey(reader.GetInt32(0), reader.GetString(1))] = reader.GetInt32(2);
        }

        return map;
    }

    private static async Task<Dictionary<string, int>> LoadTaxCodeMapAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId)
    {
        const string sql = @"
    SELECT code, tax_code_id
    FROM tax_codes
    WHERE company_id = @company_id";

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] = reader.GetInt32(1);
        }

        return map;
    }

    private static async Task<Dictionary<string, int>> LoadPartnerCodeMapAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId)
    {
        const string sql = @"
    SELECT code, partner_id
    FROM business_partners
    WHERE company_id = @company_id";

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] = reader.GetInt32(1);
        }

        return map;
    }

    private static string BuildSubAccountKey(int accountId, string subAccountCode)
    {
        return $"{accountId}:{subAccountCode.Trim()}";
    }

    private static async Task<string> GetNextEntryNumberAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        DateTime entryDate)
    {
        var prefix = $"J{entryDate:yyyyMMdd}-";
        const string sql = @"
    SELECT MAX(entry_number)
    FROM journal_vouchers
    WHERE company_id = @company_id
      AND entry_number LIKE @prefix";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("prefix", prefix + "%");
        var result = await command.ExecuteScalarAsync();
        var maxNumber = result == DBNull.Value ? null : Convert.ToString(result);

        var next = 1;
        if (!string.IsNullOrWhiteSpace(maxNumber) &&
            TryExtractEntryNumberSequence(maxNumber, prefix, out var current))
        {
            next = current + 1;
        }

        return prefix + next.ToString("0000");
    }

    private sealed record ImportVoucherBatch(string? EntryNumber, IReadOnlyList<JournalCsvRow> Rows);

    private static async Task InsertJournalLineAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long voucherId,
            int companyId,
            int lineNo,
            JournalLineInput line)
    {
        const string sql = @"
    INSERT INTO journal_lines (
        voucher_id, company_id, line_no, side, account_id, sub_account_id,
        amount, tax_code_id, tax_rate, tax_amount, creditable_tax_amount, non_creditable_tax_amount,
        tax_input_type, description,
        partner_id, invoice_number, invoice_registration_number, invoice_status, purchase_credit_rate,
        updated_at
    )
    VALUES (
        @voucher_id, @company_id, @line_no, @side, @account_id, @sub_account_id,
        @amount, @tax_code_id, @tax_rate, @tax_amount, @creditable_tax_amount, @non_creditable_tax_amount,
        @tax_input_type, @description,
        @partner_id, @invoice_number, @invoice_registration_number, @invoice_status, @purchase_credit_rate,
        CURRENT_TIMESTAMP
    )";

        if (line.Side is not ("debit" or "credit"))
        {
            throw new InvalidOperationException("借方または貸方を指定してください。");
        }

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("voucher_id", voucherId);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("line_no", lineNo);
        command.Parameters.AddWithValue("side", line.Side);
        command.Parameters.AddWithValue("account_id", line.AccountId);
        command.Parameters.AddWithValue("sub_account_id", line.SubAccountId.GetValueOrDefault());
        command.Parameters.AddWithValue("amount", line.Amount);
        command.Parameters.AddWithValue("tax_code_id", (object?)(line.TaxCodeId) ?? DBNull.Value);
        command.Parameters.AddWithValue("tax_rate", (object?)(line.TaxRate) ?? DBNull.Value);
        command.Parameters.AddWithValue("tax_amount", line.TaxAmount);
        command.Parameters.AddWithValue("creditable_tax_amount", line.CreditableTaxAmount);
        command.Parameters.AddWithValue("non_creditable_tax_amount", line.NonCreditableTaxAmount);
        command.Parameters.AddWithValue("tax_input_type", line.TaxInputType);
        command.Parameters.AddWithValue("description", (object?)(string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("partner_id", (object?)(line.PartnerId) ?? DBNull.Value);
        command.Parameters.AddWithValue("invoice_number", (object?)(string.IsNullOrWhiteSpace(line.InvoiceNumber) ? null : line.InvoiceNumber.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("invoice_registration_number", (object?)(string.IsNullOrWhiteSpace(line.InvoiceRegistrationNumber) ? null : line.InvoiceRegistrationNumber.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("invoice_status", (object?)(string.IsNullOrWhiteSpace(line.InvoiceStatus) ? null : line.InvoiceStatus) ?? DBNull.Value);
        command.Parameters.AddWithValue("purchase_credit_rate", (object?)(line.PurchaseCreditRate) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertJournalAttachmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long voucherId,
        int companyId,
        JournalVoucherAttachment attachment)
    {
        const string sql = @"
    INSERT INTO journal_voucher_attachments (
        voucher_id, company_id, file_name, content_type, content
    )
    VALUES (
        @voucher_id, @company_id, @file_name, @content_type, @content
    )";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("voucher_id", voucherId);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("file_name", attachment.FileName.Trim());
        command.Parameters.AddWithValue("content_type", (object?)attachment.ContentType ?? DBNull.Value);
        command.Parameters.Add("content", SqliteType.Blob).Value = attachment.Content;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureJournalAttachmentSchemaAsync(SqliteConnection connection, SqliteTransaction? transaction)
    {
        const string sql = @"
    CREATE TABLE IF NOT EXISTS journal_voucher_attachments (
        attachment_id INTEGER PRIMARY KEY AUTOINCREMENT,
        voucher_id    INTEGER NOT NULL REFERENCES journal_vouchers(voucher_id) ON DELETE CASCADE,
        company_id    INTEGER NOT NULL REFERENCES companies(company_id),
        file_name     VARCHAR(255) NOT NULL,
        content_type  VARCHAR(100),
        content       BLOB NOT NULL,
        created_at    TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    );
    CREATE INDEX IF NOT EXISTS idx_journal_voucher_attachments_voucher
        ON journal_voucher_attachments(voucher_id);";

        await using var command = new SqliteCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteAnnualCarryForwardExecutionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        string entryNumber)
    {
        const string sql = @"
    DELETE FROM annual_carry_forwards
    WHERE company_id = @company_id
      AND entry_number = @entry_number";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("entry_number", entryNumber.Trim());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteJournalVoucherAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId, string entryNumber)
    {
        const string voucherSql = "DELETE FROM journal_vouchers WHERE company_id = @company_id AND entry_number = @entry_number";
        await using (var voucherCommand = new SqliteCommand(voucherSql, connection, transaction))
        {
            voucherCommand.Parameters.AddWithValue("company_id", companyId);
            voucherCommand.Parameters.AddWithValue("entry_number", entryNumber.Trim());
            return await voucherCommand.ExecuteNonQueryAsync();
        }
    }

    private static void ValidateVoucherLines(IReadOnlyList<JournalLineInput> lines)
    {
        var debitTotal = lines.Where(x => x.Side == "debit").Sum(x => x.Amount);
        var creditTotal = lines.Where(x => x.Side == "credit").Sum(x => x.Amount);
        if (lines.Count < 2 || debitTotal <= 0 || debitTotal != creditTotal)
        {
            throw new InvalidOperationException("借方合計と貸方合計が一致する複数行の仕訳を入力してください。");
        }
    }

    private static void ValidateEntryNumber(string entryNumber)
    {
        if (string.IsNullOrWhiteSpace(entryNumber))
        {
            throw new InvalidOperationException("伝票番号を入力してください。");
        }

        if (!TryExtractEntryNumberSequence(entryNumber.Trim(), null, out _))
        {
            throw new InvalidOperationException("伝票番号の下4桁は数字で入力してください。");
        }
    }

    private static string IncrementEntryNumber(string entryNumber)
    {
        if (!TryExtractEntryNumberSequence(entryNumber.Trim(), null, out var sequence, out var prefix))
        {
            throw new InvalidOperationException("伝票番号の下4桁は数字で入力してください。");
        }

        return prefix + (sequence + 1).ToString("0000");
    }

    private static bool TryExtractEntryNumberSequence(string entryNumber, string? requiredPrefix, out int sequence)
    {
        return TryExtractEntryNumberSequence(entryNumber, requiredPrefix, out sequence, out _);
    }

    private static bool TryExtractEntryNumberSequence(string entryNumber, string? requiredPrefix, out int sequence, out string prefix)
    {
        sequence = 0;
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(entryNumber) || entryNumber.Length < 4)
        {
            return false;
        }

        prefix = entryNumber[..^4];
        if (requiredPrefix is not null && !string.Equals(prefix, requiredPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(entryNumber[^4..], out sequence);
    }
}

