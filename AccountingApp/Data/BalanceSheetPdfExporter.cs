using AccountingApp.Models;
using SkiaSharp;

namespace AccountingApp.Data;

public static class BalanceSheetPdfExporter
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 28f;
    private const float ColumnGap = 18f;
    private const float HeaderHeight = 24f;
    private const float RowHeight = 16f;
    private const float TitleFontSize = 18f;
    private const float HeaderFontSize = 10f;
    private const float BodyFontSize = 9.5f;
    private const float SmallFontSize = 8.5f;

    public static Task ExportAsync(
        string outputPath,
        string companyName,
        DateTime asOfDate,
        BalanceSheetSummary summary)
    {
        return Task.Run(() => ExportCore(outputPath, companyName, asOfDate, summary));
    }

    private static void ExportCore(
        string outputPath,
        string companyName,
        DateTime asOfDate,
        BalanceSheetSummary summary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var document = SKDocument.CreatePdf(stream);
        using var typeface = PdfTypefaceProvider.LoadJapaneseTypeface();

        var leftItems = BuildAssetItems(summary.Rows);
        var rightItems = BuildLiabilityAndEquityItems(summary.Rows, summary.CurrentPeriodNetIncome);

        var leftIndex = 0;
        var rightIndex = 0;

        while (leftIndex < leftItems.Count || rightIndex < rightItems.Count)
        {
            using var canvas = document.BeginPage(PageWidth, PageHeight);
            canvas.Clear(SKColors.White);

            DrawPageHeader(canvas, typeface, companyName, asOfDate);

            var contentTop = Margin + 48f;
            var leftX = Margin;
            var columnWidth = (PageWidth - Margin * 2 - ColumnGap) / 2;
            var rightX = leftX + columnWidth + ColumnGap;
            var defaultContentBottom = PageHeight - Margin;
            var maxRowsPerPage = (int)Math.Floor((defaultContentBottom - (contentTop + HeaderHeight)) / RowHeight);
            var isFinalPage = leftItems.Count - leftIndex <= maxRowsPerPage
                && rightItems.Count - rightIndex <= maxRowsPerPage;

            if (isFinalPage)
            {
                var leftRows = leftItems.Count - leftIndex;
                var rightRows = rightItems.Count - rightIndex;
                var contentBottom = contentTop + HeaderHeight + Math.Max(leftRows, rightRows) * RowHeight;

                DrawColumnFrame(canvas, typeface, leftX, contentTop, columnWidth, contentBottom, "項", "金額");
                DrawColumnFrame(canvas, typeface, rightX, contentTop, columnWidth, contentBottom, "項", "金額");

                DrawFinalPageItems(canvas, typeface, leftItems, leftIndex, leftX, contentTop + HeaderHeight, columnWidth, contentBottom);
                DrawFinalPageItems(canvas, typeface, rightItems, rightIndex, rightX, contentTop + HeaderHeight, columnWidth, contentBottom);
                leftIndex = leftItems.Count;
                rightIndex = rightItems.Count;
            }
            else
            {
                DrawColumnFrame(canvas, typeface, leftX, contentTop, columnWidth, defaultContentBottom, "項", "金額");
                DrawColumnFrame(canvas, typeface, rightX, contentTop, columnWidth, defaultContentBottom, "項", "金額");

                leftIndex = DrawItems(canvas, typeface, leftItems, leftIndex, leftX, contentTop + HeaderHeight, columnWidth, defaultContentBottom);
                rightIndex = DrawItems(canvas, typeface, rightItems, rightIndex, rightX, contentTop + HeaderHeight, columnWidth, defaultContentBottom);
            }

            document.EndPage();
        }

        document.Close();
    }

    private static List<PdfRowItem> BuildAssetItems(IReadOnlyList<BalanceSheetRow> rows)
    {
        var items = new List<PdfRowItem>();
        var groups = rows
            .Where(x => x.StatementSection == "資産の部")
            .GroupBy(x => x.ClassificationName)
            .OrderBy(x => x.Min(y => y.ClassificationSortOrder));

        var romanIndex = 1;
        foreach (var group in groups)
        {
            items.Add(new PdfRowItem($"{ToRoman(romanIndex++)} {group.Key}", null, PdfRowKind.Section));
            foreach (var row in group)
            {
                items.Add(new PdfRowItem(row.AccountName, row.ReportBalance, PdfRowKind.Detail));
            }

            items.Add(new PdfRowItem($"{group.Key}合計", group.Sum(x => x.ReportBalance), PdfRowKind.Subtotal));
        }

        items.Add(new PdfRowItem("資産合計", rows.Where(x => x.StatementSection == "資産の部").Sum(x => x.ReportBalance), PdfRowKind.GrandTotal));
        return items;
    }

    private static List<PdfRowItem> BuildLiabilityAndEquityItems(IReadOnlyList<BalanceSheetRow> rows, decimal currentPeriodNetIncome)
    {
        var items = new List<PdfRowItem>();

        var liabilities = rows.Where(x => x.StatementSection == "負債の部").ToList();
        var liabilityGroups = liabilities
            .GroupBy(x => x.ClassificationName)
            .OrderBy(x => x.Min(y => y.ClassificationSortOrder))
            .ToList();

        var romanIndex = 1;
        foreach (var group in liabilityGroups)
        {
            items.Add(new PdfRowItem($"{ToRoman(romanIndex++)} {group.Key}", null, PdfRowKind.Section));
            foreach (var row in group)
            {
                items.Add(new PdfRowItem(row.AccountName, row.ReportBalance, PdfRowKind.Detail));
            }

            items.Add(new PdfRowItem($"{group.Key}合計", group.Sum(x => x.ReportBalance), PdfRowKind.Subtotal));
        }

        items.Add(new PdfRowItem("負債合計", liabilities.Sum(x => x.ReportBalance), PdfRowKind.GrandTotal));

        var equityRows = rows.Where(x => x.StatementSection == "資本の部").ToList();
        items.Add(new PdfRowItem("資本の部", null, PdfRowKind.SectionDivider));

        var equityGroups = equityRows
            .GroupBy(x => x.ClassificationName)
            .OrderBy(x => x.Min(y => y.ClassificationSortOrder))
            .ToList();

        romanIndex = 1;
        foreach (var group in equityGroups)
        {
            items.Add(new PdfRowItem($"{ToRoman(romanIndex++)} {group.Key}", null, PdfRowKind.Section));
            foreach (var row in group)
            {
                items.Add(new PdfRowItem(row.AccountName, row.ReportBalance, PdfRowKind.Detail));
            }

            items.Add(new PdfRowItem($"{group.Key}合計", group.Sum(x => x.ReportBalance), PdfRowKind.Subtotal));
        }

        items.Add(new PdfRowItem("当期純損益", currentPeriodNetIncome, PdfRowKind.Highlight));
        var equityTotal = equityRows.Sum(x => x.ReportBalance) + currentPeriodNetIncome;
        items.Add(new PdfRowItem("資本の部合計", equityTotal, PdfRowKind.GrandTotal));
        items.Add(new PdfRowItem("負債・純資産合計", liabilities.Sum(x => x.ReportBalance) + equityTotal, PdfRowKind.GrandTotal));

        return items;
    }

    private static int DrawItems(
        SKCanvas canvas,
        SKTypeface typeface,
        List<PdfRowItem> items,
        int startIndex,
        float x,
        float startY,
        float width,
        float bottom)
    {
        var y = startY;
        var amountWidth = 92f;
        var textWidth = width - amountWidth;

        for (var i = startIndex; i < items.Count; i++)
        {
            if (y + RowHeight > bottom)
            {
                return i;
            }

            var item = items[i];
            DrawItemRow(canvas, typeface, item, x, y, textWidth, amountWidth);
            y += RowHeight;
        }

        return items.Count;
    }

    private static void DrawFinalPageItems(
        SKCanvas canvas,
        SKTypeface typeface,
        List<PdfRowItem> items,
        int startIndex,
        float x,
        float startY,
        float width,
        float bottom)
    {
        var pageItems = items.Skip(startIndex).ToList();
        if (pageItems.Count == 0)
        {
            return;
        }

        var amountWidth = 92f;
        var textWidth = width - amountWidth;
        var totalItem = pageItems[^1];
        var detailItems = pageItems.Take(pageItems.Count - 1).ToList();
        var y = startY;

        foreach (var item in detailItems)
        {
            DrawItemRow(canvas, typeface, item, x, y, textWidth, amountWidth);
            y += RowHeight;
        }

        DrawItemRow(canvas, typeface, totalItem, x, bottom - RowHeight, textWidth, amountWidth);
    }

    private static void DrawItemRow(
        SKCanvas canvas,
        SKTypeface typeface,
        PdfRowItem item,
        float x,
        float y,
        float textWidth,
        float amountWidth)
    {
        var rowRect = new SKRect(x, y, x + textWidth + amountWidth, y + RowHeight);

        using var borderPaint = new SKPaint
        {
            Color = SKColor.Parse("#1F2937"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.8f
        };

        using var fillPaint = new SKPaint
        {
            Color = GetBackground(item.Kind),
            Style = SKPaintStyle.Fill
        };

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#111827")
        };

        using var textFont = new SKFont(typeface, item.Kind is PdfRowKind.Section or PdfRowKind.SectionDivider ? HeaderFontSize : BodyFontSize)
        {
            Embolden = item.Kind is PdfRowKind.Section or PdfRowKind.Subtotal or PdfRowKind.GrandTotal or PdfRowKind.Highlight or PdfRowKind.SectionDivider
        };

        canvas.DrawRect(rowRect, fillPaint);
        canvas.DrawRect(rowRect, borderPaint);
        canvas.DrawLine(x + textWidth, y, x + textWidth, y + RowHeight, borderPaint);

        var baseline = y + RowHeight - 4f;
        canvas.DrawText(item.Label, x + 6f, baseline, SKTextAlign.Left, textFont, textPaint);

        if (item.Amount.HasValue)
        {
            var amount = FormatFinancialStatementAmount(item.Amount.Value);
            var amountWidthValue = textFont.MeasureText(amount, textPaint);
            canvas.DrawText(amount, x + textWidth + amountWidth - amountWidthValue - 6f, baseline, SKTextAlign.Left, textFont, textPaint);
        }
    }

    private static void DrawColumnFrame(
        SKCanvas canvas,
        SKTypeface typeface,
        float x,
        float top,
        float width,
        float bottom,
        string leftHeader,
        string rightHeader)
    {
        using var borderPaint = new SKPaint
        {
            Color = SKColor.Parse("#111827"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f
        };

        using var fillPaint = new SKPaint
        {
            Color = SKColor.Parse("#DCE7F5"),
            Style = SKPaintStyle.Fill
        };

        var frame = new SKRect(x, top, x + width, bottom);
        var headerRect = new SKRect(x, top, x + width, top + HeaderHeight);
        var amountX = x + width - 92f;

        canvas.DrawRect(frame, borderPaint);
        canvas.DrawRect(headerRect, fillPaint);
        canvas.DrawRect(headerRect, borderPaint);
        canvas.DrawLine(amountX, top, amountX, bottom, borderPaint);

        using var headerPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#111827")
        };
        using var headerFont = new SKFont(typeface, HeaderFontSize) { Embolden = true };

        canvas.DrawText(leftHeader, x + 50f, top + HeaderHeight - 7f, SKTextAlign.Left, headerFont, headerPaint);
        canvas.DrawText(rightHeader, amountX + 25f, top + HeaderHeight - 7f, SKTextAlign.Left, headerFont, headerPaint);
    }

    private static void DrawPageHeader(SKCanvas canvas, SKTypeface typeface, string companyName, DateTime asOfDate)
    {
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#111827")
        };
        using var titleFont = new SKFont(typeface, TitleFontSize) { Embolden = true };

        using var subPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#111827")
        };
        using var subFont = new SKFont(typeface, HeaderFontSize);

        using var rightPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#111827")
        };
        using var rightFont = new SKFont(typeface, SmallFontSize);

        canvas.DrawText("貸借対照表", PageWidth / 2, Margin - 4f, SKTextAlign.Center, titleFont, titlePaint);
        canvas.DrawText($"{companyName}  （{asOfDate:yyyy年M月d日現在}）", PageWidth / 2, Margin + 14f, SKTextAlign.Center, subFont, subPaint);
        canvas.DrawText("単位: 円", PageWidth - Margin, Margin + 14f, SKTextAlign.Right, rightFont, rightPaint);
    }

    private static string FormatFinancialStatementAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount).ToString("N0")}"
            : amount.ToString("N0");
    }

    private static string ToRoman(int number)
    {
        return number switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            _ => number.ToString()
        };
    }

    private static SKColor GetBackground(PdfRowKind kind)
    {
        return kind switch
        {
            PdfRowKind.Section => SKColor.Parse("#FFFFFF"),
            PdfRowKind.SectionDivider => SKColor.Parse("#DCE7F5"),
            PdfRowKind.Subtotal => SKColor.Parse("#F8FAFC"),
            PdfRowKind.Highlight => SKColor.Parse("#FFF7ED"),
            PdfRowKind.GrandTotal => SKColor.Parse("#EAF1E8"),
            _ => SKColors.White
        };
    }

    private sealed record PdfRowItem(string Label, decimal? Amount, PdfRowKind Kind);

    private enum PdfRowKind
    {
        Detail,
        Section,
        SectionDivider,
        Subtotal,
        Highlight,
        GrandTotal
    }
}
