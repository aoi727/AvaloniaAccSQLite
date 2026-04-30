using AccountingApp.Models;
using SkiaSharp;

namespace AccountingApp.Data;

public static class ProfitAndLossPdfExporter
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 32f;
    private const float TableTop = 132f;
    private const float RowHeight = 15f;
    private const float LabelWidth = 290f;
    private const float AmountColumnWidth = 100f;
    private const float TitleFontSize = 16f;
    private const float HeaderFontSize = 9f;
    private const float BodyFontSize = 8.4f;
    private const float DetailIndent = 14f;
    private const float TableLineWidth = 1f;

    public static Task ExportAsync(
        string outputPath,
        string companyName,
        DateTime fromDate,
        DateTime toDate,
        ProfitAndLossSummary summary)
    {
        return Task.Run(() => ExportCore(outputPath, companyName, fromDate, toDate, summary));
    }

    private static void ExportCore(
        string outputPath,
        string companyName,
        DateTime fromDate,
        DateTime toDate,
        ProfitAndLossSummary summary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var document = SKDocument.CreatePdf(stream);
        using var typeface = PdfTypefaceProvider.LoadJapaneseTypeface();

        var rows = BuildRows(summary);
        using var canvas = document.BeginPage(PageWidth, PageHeight);
        canvas.Clear(SKColors.White);

        DrawPageHeader(canvas, typeface, companyName, fromDate, toDate);
        DrawTable(canvas, typeface, rows);

        document.EndPage();
        document.Close();
    }

    private static List<ProfitAndLossPdfRow> BuildRows(ProfitAndLossSummary summary)
    {
        var rows = new List<ProfitAndLossPdfRow>();

        var netSalesDetails = BuildDetailRows(summary.Rows, "売上高").ToList();
        rows.AddRange(netSalesDetails);
        rows.Add(new("売上高", null, summary.NetSales, ProfitAndLossPdfRowKind.Stage, netSalesDetails.Count > 0));

        rows.Add(new("売上原価", null, null, ProfitAndLossPdfRowKind.Section));
        rows.AddRange(BuildDetailRows(summary.Rows, "売上原価"));
        rows.Add(new("売上原価計", summary.CostOfSales, null, ProfitAndLossPdfRowKind.Subtotal, true));

        rows.Add(new("売上総利益", null, summary.GrossProfit, ProfitAndLossPdfRowKind.Stage, true));

        rows.Add(new("販売費及び一般管理費", null, null, ProfitAndLossPdfRowKind.Section));
        rows.AddRange(BuildDetailRows(summary.Rows, "販売費及び一般管理費"));
        rows.Add(new("販売費及び一般管理費計", summary.SellingGeneralAdministrativeExpenses, null, ProfitAndLossPdfRowKind.Subtotal, true));

        rows.Add(new("営業利益", null, summary.OperatingProfit, ProfitAndLossPdfRowKind.Stage, true));

        rows.Add(new("営業外収益", null, null, ProfitAndLossPdfRowKind.Section));
        rows.AddRange(BuildDetailRows(summary.Rows, "営業外収益・特別利益"));
        rows.Add(new("営業外収益計", summary.NonOperatingAndSpecialGains, null, ProfitAndLossPdfRowKind.Subtotal, true));

        rows.Add(new("営業外費用", null, null, ProfitAndLossPdfRowKind.Section));
        rows.AddRange(BuildDetailRows(summary.Rows, "営業外費用・特別損失"));
        rows.Add(new("営業外費用計", summary.NonOperatingAndSpecialLosses, null, ProfitAndLossPdfRowKind.Subtotal, true));

        rows.Add(new("当期純利益", null, summary.NetIncome, ProfitAndLossPdfRowKind.FinalTotal, true));

        return rows;
    }

    private static IEnumerable<ProfitAndLossPdfRow> BuildDetailRows(IEnumerable<ProfitAndLossRow> rows, string sectionName)
    {
        foreach (var row in rows.Where(x => x.StatementSection == sectionName && x.ReportAmount != 0))
        {
            yield return new ProfitAndLossPdfRow(row.AccountName, row.ReportAmount, null, ProfitAndLossPdfRowKind.Detail);
        }
    }

    private static void DrawTable(SKCanvas canvas, SKTypeface typeface, IReadOnlyList<ProfitAndLossPdfRow> rows)
    {
        var x = Margin;
        var y = TableTop;
        var totalWidth = LabelWidth + AmountColumnWidth * 2;
        var bottom = y + rows.Count * RowHeight;
        var amount1X = x + LabelWidth;
        var amount2X = amount1X + AmountColumnWidth;

        using var borderPaint = new SKPaint
        {
            Color = SKColor.Parse("#111827"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = TableLineWidth,
            IsAntialias = false
        };
        using var fillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = SKColor.Parse("#111827"), IsAntialias = true };
        using var bodyFont = new SKFont(typeface, BodyFontSize);
        using var boldFont = new SKFont(typeface, BodyFontSize) { Embolden = true };

        var outer = new SKRect(x, y, x + totalWidth, bottom);
        canvas.DrawRect(outer, fillPaint);
        canvas.DrawRect(outer, borderPaint);

        canvas.DrawLine(amount1X, y, amount1X, bottom, borderPaint);
        canvas.DrawLine(amount2X, y, amount2X, bottom, borderPaint);

        var currentY = y;
        foreach (var row in rows)
        {
            var font = row.Kind is ProfitAndLossPdfRowKind.Detail ? bodyFont : boldFont;
            var baseline = currentY + RowHeight - 5f;
            var labelX = x + 8f + (row.Kind == ProfitAndLossPdfRowKind.Detail ? DetailIndent : 0f);
            canvas.DrawText(row.Label, labelX, baseline, SKTextAlign.Left, font, textPaint);

            if (row.LeftAmount.HasValue)
            {
                var text = FormatFinancialStatementAmount(row.LeftAmount.Value);
                var width = font.MeasureText(text, textPaint);
                canvas.DrawText(text, amount1X + AmountColumnWidth - width - 6f, baseline, SKTextAlign.Left, font, textPaint);
                if (row.ShowAmountRule && row.Kind is ProfitAndLossPdfRowKind.Subtotal)
                {
                    var lineY = currentY + 2f;
                    canvas.DrawLine(amount1X + 18f, lineY, amount1X + AmountColumnWidth - 6f, lineY, borderPaint);
                }
            }

            if (row.RightAmount.HasValue)
            {
                var text = FormatFinancialStatementAmount(row.RightAmount.Value);
                var width = font.MeasureText(text, textPaint);
                canvas.DrawText(text, amount2X + AmountColumnWidth - width - 6f, baseline, SKTextAlign.Left, font, textPaint);
                if (row.ShowAmountRule && (row.Kind is ProfitAndLossPdfRowKind.Stage or ProfitAndLossPdfRowKind.FinalTotal))
                {
                    var lineY = currentY + 2f;
                    canvas.DrawLine(amount2X + 18f, lineY, amount2X + AmountColumnWidth - 6f, lineY, borderPaint);
                }
            }

            currentY += RowHeight;
        }
    }

    private static void DrawPageHeader(SKCanvas canvas, SKTypeface typeface, string companyName, DateTime fromDate, DateTime toDate)
    {
        using var titlePaint = new SKPaint { Color = SKColor.Parse("#111827"), IsAntialias = true };
        using var bodyPaint = new SKPaint { Color = SKColor.Parse("#111827"), IsAntialias = true };
        using var titleFont = new SKFont(typeface, TitleFontSize) { Embolden = true };
        using var bodyFont = new SKFont(typeface, HeaderFontSize);

        canvas.DrawText("損益計算書", PageWidth / 2, 48f, SKTextAlign.Center, titleFont, titlePaint);
        canvas.DrawLine(PageWidth / 2 - 62f, 53f, PageWidth / 2 + 62f, 53f, bodyPaint);
        canvas.DrawText(
            $"{fromDate:yyyy年M月d日} から {toDate:yyyy年M月d日} まで",
            PageWidth / 2,
            72f,
            SKTextAlign.Center,
            bodyFont,
            bodyPaint);
        canvas.DrawText(companyName, Margin + 4f, 116f, SKTextAlign.Left, bodyFont, bodyPaint);
        canvas.DrawText("(単位：円)", PageWidth - Margin, 116f, SKTextAlign.Right, bodyFont, bodyPaint);
    }

    private static string FormatFinancialStatementAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount).ToString("N0")}"
            : amount.ToString("N0");
    }

    private sealed record ProfitAndLossPdfRow(
        string Label,
        decimal? LeftAmount,
        decimal? RightAmount,
        ProfitAndLossPdfRowKind Kind,
        bool ShowAmountRule = false);

    private enum ProfitAndLossPdfRowKind
    {
        Detail,
        Section,
        Subtotal,
        Stage,
        FinalTotal
    }
}
