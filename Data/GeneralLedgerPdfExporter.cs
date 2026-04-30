using AccountingApp.Models;
using SkiaSharp;

namespace AccountingApp.Data;

public static class GeneralLedgerPdfExporter
{
    private const float PageWidth = 842f;
    private const float PageHeight = 595f;
    private const float Margin = 24f;
    private const float HeaderTop = 96f;
    private const float HeaderHeight = 26f;
    private const float RowHeight = 18f;
    private const float TitleFontSize = 18f;
    private const float HeaderFontSize = 9.5f;
    private const float BodyFontSize = 8.5f;
    private const float LineWidth = 0.9f;

    public static Task ExportAsync(
        string outputPath,
        string companyName,
        string accountLabel,
        string subAccountLabel,
        string periodLabel,
        decimal carryForward,
        IReadOnlyList<GeneralLedgerLine> lines)
    {
        return Task.Run(() => ExportCore(outputPath, companyName, accountLabel, subAccountLabel, periodLabel, carryForward, lines));
    }

    private static void ExportCore(
        string outputPath,
        string companyName,
        string accountLabel,
        string subAccountLabel,
        string periodLabel,
        decimal carryForward,
        IReadOnlyList<GeneralLedgerLine> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var document = SKDocument.CreatePdf(stream);
        using var typeface = PdfTypefaceProvider.LoadJapaneseTypeface();

        var columns = new[] { 74f, 90f, 148f, 128f, 168f, 58f, 58f, 70f };
        var totalWidth = columns.Sum();
        var contentTop = HeaderTop + HeaderHeight;
        var contentBottom = PageHeight - Margin;
        var rowsPerPage = Math.Max(1, (int)Math.Floor((contentBottom - contentTop) / RowHeight));

        var allRows = new List<LedgerPdfRow>
        {
            new("繰越残高", string.Empty, string.Empty, string.Empty, string.Empty, null, null, carryForward, true)
        };
        allRows.AddRange(lines.Select(line => new LedgerPdfRow(
            line.EntryDate.ToString("MM/dd"),
            line.EntryNumber,
            BuildCounterpart(line),
            line.Description ?? string.Empty,
            BuildPartner(line),
            line.Debit == 0 ? null : line.Debit,
            line.Credit == 0 ? null : line.Credit,
            line.Balance,
            false)));

        var pageCount = Math.Max(1, (int)Math.Ceiling(allRows.Count / (double)rowsPerPage));
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            using var canvas = document.BeginPage(PageWidth, PageHeight);
            canvas.Clear(SKColors.White);

            DrawHeader(canvas, typeface, companyName, accountLabel, subAccountLabel, periodLabel, pageIndex + 1, pageCount);
            DrawTableHeader(canvas, typeface, columns, totalWidth);

            var pageRows = allRows.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList();
            DrawRows(canvas, typeface, columns, totalWidth, contentTop, pageRows);

            if (pageIndex == pageCount - 1)
            {
                DrawTotals(canvas, typeface, columns, totalWidth, contentTop + pageRows.Count * RowHeight, lines);
            }

            document.EndPage();
        }

        document.Close();
    }

    private static void DrawHeader(
        SKCanvas canvas,
        SKTypeface typeface,
        string companyName,
        string accountLabel,
        string subAccountLabel,
        string periodLabel,
        int pageNumber,
        int pageCount)
    {
        using var titlePaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var bodyPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var titleFont = new SKFont(typeface, TitleFontSize) { Embolden = true };
        using var bodyFont = new SKFont(typeface, HeaderFontSize);

        canvas.DrawText("総勘定元帳", PageWidth / 2, 36f, SKTextAlign.Center, titleFont, titlePaint);
        canvas.DrawText(companyName, Margin, 56f, SKTextAlign.Left, bodyFont, bodyPaint);
        canvas.DrawText($"勘定科目: {accountLabel}", Margin, 72f, SKTextAlign.Left, bodyFont, bodyPaint);
        canvas.DrawText($"補助科目: {subAccountLabel}", PageWidth / 2, 72f, SKTextAlign.Center, bodyFont, bodyPaint);
        canvas.DrawText($"期間: {periodLabel}", Margin, 88f, SKTextAlign.Left, bodyFont, bodyPaint);
        canvas.DrawText($"ページ {pageNumber}/{pageCount}", PageWidth - Margin, 56f, SKTextAlign.Right, bodyFont, bodyPaint);
    }

    private static void DrawTableHeader(SKCanvas canvas, SKTypeface typeface, IReadOnlyList<float> columns, float totalWidth)
    {
        using var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth, IsAntialias = false };
        using var headerPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var headerFont = new SKFont(typeface, HeaderFontSize) { Embolden = true };

        var top = HeaderTop;
        var bottom = top + HeaderHeight;
        canvas.DrawRect(new SKRect(Margin, top, Margin + totalWidth, bottom), borderPaint);

        float runningX = Margin;
        foreach (var width in columns.Take(columns.Count - 1))
        {
            runningX += width;
            canvas.DrawLine(runningX, top, runningX, bottom, borderPaint);
        }

        var labels = new[] { "日付", "仕訳番号", "相手科目", "摘要", "取引先/請求書", "借方", "貸方", "残高" };
        runningX = Margin;
        for (var i = 0; i < columns.Count; i++)
        {
            var align = i >= 5 ? SKTextAlign.Right : SKTextAlign.Left;
            var x = align == SKTextAlign.Right ? runningX + columns[i] - 6f : runningX + 6f;
            canvas.DrawText(labels[i], x, bottom - 8f, align, headerFont, headerPaint);
            runningX += columns[i];
        }
    }

    private static void DrawRows(SKCanvas canvas, SKTypeface typeface, IReadOnlyList<float> columns, float totalWidth, float top, IReadOnlyList<LedgerPdfRow> rows)
    {
        using var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth, IsAntialias = false };
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var bodyFont = new SKFont(typeface, BodyFontSize);
        using var boldFont = new SKFont(typeface, BodyFontSize) { Embolden = true };

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowTop = top + rowIndex * RowHeight;
            var rowBottom = rowTop + RowHeight;
            canvas.DrawRect(new SKRect(Margin, rowTop, Margin + totalWidth, rowBottom), borderPaint);

            float runningX = Margin;
            foreach (var width in columns.Take(columns.Count - 1))
            {
                runningX += width;
                canvas.DrawLine(runningX, rowTop, runningX, rowBottom, borderPaint);
            }

            var row = rows[rowIndex];
            var font = row.IsCarryForward ? boldFont : bodyFont;

            DrawCell(canvas, row.DateText, Margin + 6f, rowBottom - 6f, SKTextAlign.Left, font, textPaint);
            DrawCell(canvas, row.EntryNumber, Margin + columns[0] + 6f, rowBottom - 6f, SKTextAlign.Left, font, textPaint);
            DrawCell(canvas, row.Counterpart, Margin + columns[0] + columns[1] + 6f, rowBottom - 6f, SKTextAlign.Left, font, textPaint);
            DrawCell(canvas, row.Description, Margin + columns.Take(3).Sum() + 6f, rowBottom - 6f, SKTextAlign.Left, font, textPaint);
            DrawCell(canvas, row.Partner, Margin + columns.Take(4).Sum() + 6f, rowBottom - 6f, SKTextAlign.Left, font, textPaint);
            if (row.Debit.HasValue)
            {
                DrawCell(canvas, row.Debit.Value.ToString("N0"), Margin + columns.Take(6).Sum() - 6f, rowBottom - 6f, SKTextAlign.Right, font, textPaint);
            }

            if (row.Credit.HasValue)
            {
                DrawCell(canvas, row.Credit.Value.ToString("N0"), Margin + columns.Take(7).Sum() - 6f, rowBottom - 6f, SKTextAlign.Right, font, textPaint);
            }

            DrawCell(canvas, row.Balance.ToString("N0"), Margin + totalWidth - 6f, rowBottom - 6f, SKTextAlign.Right, font, textPaint);
        }
    }

    private static void DrawTotals(SKCanvas canvas, SKTypeface typeface, IReadOnlyList<float> columns, float totalWidth, float top, IReadOnlyList<GeneralLedgerLine> lines)
    {
        using var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth, IsAntialias = false };
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var bodyFont = new SKFont(typeface, BodyFontSize) { Embolden = true };

        var bottom = top + RowHeight;
        canvas.DrawRect(new SKRect(Margin, top, Margin + totalWidth, bottom), borderPaint);

        float runningX = Margin;
        foreach (var width in columns.Take(columns.Count - 1))
        {
            runningX += width;
            canvas.DrawLine(runningX, top, runningX, bottom, borderPaint);
        }

        DrawCell(canvas, "合計", Margin + 6f, bottom - 6f, SKTextAlign.Left, bodyFont, textPaint);
        DrawCell(canvas, lines.Sum(x => x.Debit).ToString("N0"), Margin + columns.Take(6).Sum() - 6f, bottom - 6f, SKTextAlign.Right, bodyFont, textPaint);
        DrawCell(canvas, lines.Sum(x => x.Credit).ToString("N0"), Margin + columns.Take(7).Sum() - 6f, bottom - 6f, SKTextAlign.Right, bodyFont, textPaint);
        var endingBalance = lines.LastOrDefault()?.Balance ?? 0m;
        DrawCell(canvas, endingBalance.ToString("N0"), Margin + totalWidth - 6f, bottom - 6f, SKTextAlign.Right, bodyFont, textPaint);
    }

    private static void DrawCell(SKCanvas canvas, string text, float x, float y, SKTextAlign align, SKFont font, SKPaint paint)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            canvas.DrawText(text, x, y, align, font, paint);
        }
    }

    private static string BuildCounterpart(GeneralLedgerLine line)
    {
        if (string.IsNullOrWhiteSpace(line.CounterpartAccountCode))
        {
            return string.Empty;
        }

        var account = $"{line.CounterpartAccountCode} {line.CounterpartAccountName}";
        return string.IsNullOrWhiteSpace(line.CounterpartSubAccountCode)
            ? account
            : $"{account}/{line.CounterpartSubAccountCode} {line.CounterpartSubAccountName}";
    }

    private static string BuildPartner(GeneralLedgerLine line)
    {
        var partner = string.IsNullOrWhiteSpace(line.PartnerCode)
            ? line.PartnerName ?? string.Empty
            : $"{line.PartnerCode} {line.PartnerName}";

        if (string.IsNullOrWhiteSpace(line.InvoiceNumber))
        {
            return partner;
        }

        return string.IsNullOrWhiteSpace(partner) ? line.InvoiceNumber : $"{partner}/{line.InvoiceNumber}";
    }

    private sealed record LedgerPdfRow(
        string DateText,
        string EntryNumber,
        string Counterpart,
        string Description,
        string Partner,
        decimal? Debit,
        decimal? Credit,
        decimal Balance,
        bool IsCarryForward);
}
