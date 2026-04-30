using AccountingApp.Models;
using SkiaSharp;

namespace AccountingApp.Data;

public static class JournalBookPdfExporter
{
    private const float PageWidth = 1189f;
    private const float PageHeight = 842f;
    private const float Margin = 28f;
    private const float HeaderTop = 78f;
    private const float HeaderHeight = 26f;
    private const float RowHeight = 18f;
    private const float TitleFontSize = 18f;
    private const float HeaderFontSize = 8.8f;
    private const float BodyFontSize = 8.2f;
    private const float LineWidth = 0.9f;
    private const float InnerSeparatorWidth = 0.55f;
    private const float VoucherSeparatorWidth = 2.0f;
    private const float CellPadding = 6f;

    public static Task<string?> ExportAsync(
        string outputPath,
        string companyName,
        DateTime targetMonth,
        IReadOnlyList<JournalBookRow> rows)
    {
        return Task.Run(() => ExportCore(outputPath, companyName, targetMonth, rows));
    }

    private static string? ExportCore(
        string outputPath,
        string companyName,
        DateTime targetMonth,
        IReadOnlyList<JournalBookRow> rows)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var document = SKDocument.CreatePdf(stream);
            using var typeface = PdfTypefaceProvider.LoadJapaneseTypeface();

            var columns = new[]
            {
                78f, 100f, 240f, 120f, 150f, 150f, 95f, 95f
            };
            var totalWidth = columns.Sum();
            var contentTop = HeaderTop + HeaderHeight;
            var contentBottom = PageHeight - Margin;
            var rowsPerPage = Math.Max(1, (int)Math.Floor((contentBottom - contentTop) / RowHeight));

            var pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                using var canvas = document.BeginPage(PageWidth, PageHeight);
                canvas.Clear(SKColors.White);

                DrawHeader(canvas, typeface, companyName, targetMonth, pageIndex + 1, pageCount);
                DrawTableHeader(canvas, typeface, columns, totalWidth);

                var pageRows = rows.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList();
                DrawRows(canvas, typeface, columns, totalWidth, contentTop, pageRows);

                if (pageIndex == pageCount - 1)
                {
                    DrawTotals(canvas, typeface, columns, totalWidth, contentTop + pageRows.Count * RowHeight, rows);
                }

                document.EndPage();
            }

            document.Close();
            return null;
        }
        catch (IOException)
        {
            return "PDFファイルに書き込めませんでした。別のアプリで開いている場合は閉じてから再実行してください。";
        }
    }

    private static void DrawHeader(SKCanvas canvas, SKTypeface typeface, string companyName, DateTime targetMonth, int pageNumber, int pageCount)
    {
        using var titlePaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var bodyPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var titleFont = new SKFont(typeface, TitleFontSize) { Embolden = true };
        using var bodyFont = new SKFont(typeface, HeaderFontSize);

        canvas.DrawText("仕訳帳", PageWidth / 2, 36f, SKTextAlign.Center, titleFont, titlePaint);
        canvas.DrawText(companyName, Margin, 58f, SKTextAlign.Left, bodyFont, bodyPaint);
        canvas.DrawText($"{targetMonth:yyyy年M月分}", PageWidth / 2, 58f, SKTextAlign.Center, bodyFont, bodyPaint);
        canvas.DrawText($"ページ {pageNumber}/{pageCount}", PageWidth - Margin, 58f, SKTextAlign.Right, bodyFont, bodyPaint);
    }

    private static void DrawTableHeader(SKCanvas canvas, SKTypeface typeface, IReadOnlyList<float> columns, float totalWidth)
    {
        using var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth, IsAntialias = false };
        using var headerPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var headerFont = new SKFont(typeface, HeaderFontSize) { Embolden = true };

        var left = Margin;
        var top = HeaderTop;
        var bottom = top + HeaderHeight;
        canvas.DrawRect(new SKRect(left, top, left + totalWidth, bottom), borderPaint);

        float runningX = left;
        foreach (var width in columns.Take(columns.Count - 1))
        {
            runningX += width;
            canvas.DrawLine(runningX, top, runningX, bottom, borderPaint);
        }

        var labels = new[] { "日付", "伝票番号", "摘要", "参照", "借方科目", "貸方科目", "借方金額", "貸方金額" };
        runningX = left;
        for (var i = 0; i < columns.Count; i++)
        {
            var align = i >= 6 ? SKTextAlign.Right : SKTextAlign.Left;
            DrawCellText(canvas, labels[i], runningX, bottom - 8f, columns[i], align, headerFont, headerPaint);
            runningX += columns[i];
        }
    }

    private static void DrawRows(SKCanvas canvas, SKTypeface typeface, IReadOnlyList<float> columns, float totalWidth, float top, IReadOnlyList<JournalBookRow> rows)
    {
        using var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth, IsAntialias = false };
        using var innerSeparatorPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = InnerSeparatorWidth,
            IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash([2.5f, 2.5f], 0)
        };
        using var voucherSeparatorPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = VoucherSeparatorWidth, IsAntialias = false };
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var bodyFont = new SKFont(typeface, BodyFontSize);
        using var boldFont = new SKFont(typeface, BodyFontSize) { Embolden = true };

        string? previousEntryNumber = null;
        string? previousDescription = null;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowTop = top + rowIndex * RowHeight;
            var rowBottom = rowTop + RowHeight;
            var row = rows[rowIndex];
            var isVoucherStart = !string.Equals(previousEntryNumber, row.EntryNumber, StringComparison.Ordinal);

            canvas.DrawLine(Margin, rowBottom, Margin + totalWidth, rowBottom, borderPaint);
            canvas.DrawLine(Margin, rowTop, Margin, rowBottom, borderPaint);
            canvas.DrawLine(Margin + totalWidth, rowTop, Margin + totalWidth, rowBottom, borderPaint);

            float runningX = Margin;
            foreach (var width in columns.Take(columns.Count - 1))
            {
                runningX += width;
                canvas.DrawLine(runningX, rowTop, runningX, rowBottom, borderPaint);
            }

            if (isVoucherStart)
            {
                canvas.DrawLine(Margin, rowTop, Margin + totalWidth, rowTop, voucherSeparatorPaint);
            }
            else
            {
                canvas.DrawLine(Margin, rowTop, Margin + totalWidth, rowTop, innerSeparatorPaint);
            }

            var values = new[]
            {
                isVoucherStart ? row.EntryDate.ToString("yyyy/MM/dd") : string.Empty,
                isVoucherStart ? row.EntryNumber : string.Empty,
                ResolveDescriptionText(row.Description, isVoucherStart, previousDescription),
                isVoucherStart ? row.Reference ?? string.Empty : string.Empty,
                row.DebitAccountDisplay ?? string.Empty,
                row.CreditAccountDisplay ?? string.Empty,
                row.DebitAmount == 0 ? string.Empty : row.DebitAmount.ToString("N0"),
                row.CreditAmount == 0 ? string.Empty : row.CreditAmount.ToString("N0")
            };

            runningX = Margin;
            for (var i = 0; i < columns.Count; i++)
            {
                var align = i >= 6 ? SKTextAlign.Right : SKTextAlign.Left;
                var font = i == 1 ? boldFont : bodyFont;
                DrawCellText(canvas, values[i], runningX, rowBottom - 6f, columns[i], align, font, textPaint);
                runningX += columns[i];
            }

            previousEntryNumber = row.EntryNumber;
            previousDescription = row.Description;
        }
    }

    private static void DrawTotals(SKCanvas canvas, SKTypeface typeface, IReadOnlyList<float> columns, float totalWidth, float top, IReadOnlyList<JournalBookRow> rows)
    {
        using var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth, IsAntialias = false };
        using var totalTopPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = VoucherSeparatorWidth, IsAntialias = false };
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var bodyFont = new SKFont(typeface, BodyFontSize) { Embolden = true };

        var rowBottom = top + RowHeight;
        canvas.DrawRect(new SKRect(Margin, top, Margin + totalWidth, rowBottom), borderPaint);
        canvas.DrawLine(Margin, top, Margin + totalWidth, top, totalTopPaint);

        float runningX = Margin;
        foreach (var width in columns.Take(columns.Count - 1))
        {
            runningX += width;
            canvas.DrawLine(runningX, top, runningX, rowBottom, borderPaint);
        }

        var debitTotal = rows.Sum(x => x.DebitAmount).ToString("N0");
        var creditTotal = rows.Sum(x => x.CreditAmount).ToString("N0");

        DrawCellText(canvas, "合計", Margin, rowBottom - 6f, columns[0], SKTextAlign.Left, bodyFont, textPaint);

        var debitLeft = Margin + columns.Take(6).Sum();
        DrawCellText(canvas, debitTotal, debitLeft, rowBottom - 6f, columns[6], SKTextAlign.Right, bodyFont, textPaint);
        DrawCellText(canvas, creditTotal, debitLeft + columns[6], rowBottom - 6f, columns[7], SKTextAlign.Right, bodyFont, textPaint);
    }

    private static void DrawCellText(
        SKCanvas canvas,
        string text,
        float left,
        float baseline,
        float width,
        SKTextAlign align,
        SKFont font,
        SKPaint paint)
    {
        var maxWidth = Math.Max(0, width - (CellPadding * 2));
        var fitted = FitText(text, font, paint, maxWidth);
        var x = align == SKTextAlign.Right ? left + width - CellPadding : left + CellPadding;
        canvas.DrawText(fitted, x, baseline, align, font, paint);
    }

    private static string FitText(string? text, SKFont font, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return string.Empty;
        }

        if (font.MeasureText(text, paint) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        if (font.MeasureText(ellipsis, paint) > maxWidth)
        {
            return string.Empty;
        }

        var length = text.Length;
        while (length > 0)
        {
            var candidate = text[..length] + ellipsis;
            if (font.MeasureText(candidate, paint) <= maxWidth)
            {
                return candidate;
            }

            length--;
        }

        return ellipsis;
    }

    private static string ResolveDescriptionText(string? description, bool isVoucherStart, string? previousDescription)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        if (!isVoucherStart && string.Equals(description, previousDescription, StringComparison.Ordinal))
        {
            return "〃";
        }

        return description;
    }

}
