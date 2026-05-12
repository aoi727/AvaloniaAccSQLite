using ReligiousReportApp.Models;
using SkiaSharp;

namespace ReligiousReportApp.Data;

public static class ReligiousReportPdfExporter
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 36f;
    private const float HeaderTop = 42f;
    private const float TableTop = 132f;
    private const float RowHeight = 18f;
    private const float CodeWidth = 70f;
    private const float LabelWidth = 205f;
    private const float AmountWidth = 82f;
    private const float TableWidth = CodeWidth + LabelWidth + AmountWidth * 3;
    private const float NoteLineHeight = 14f;
    private const float BodyFontSize = 8.8f;
    private const float HeaderFontSize = 9.4f;
    private const float TitleFontSize = 16f;

    public static Task ExportAsync(
        string outputPath,
        string companyName,
        ReligiousReportSummary summary,
        string? note)
    {
        return Task.Run(() => ExportCore(outputPath, companyName, summary, note ?? ""));
    }

    private static void ExportCore(
        string outputPath,
        string companyName,
        ReligiousReportSummary summary,
        string note)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var document = SKDocument.CreatePdf(stream);
        using var typeface = PdfTypefaceProvider.LoadJapaneseTypeface();
        using var paints = new PdfPaintSet(typeface);

        var rows = BuildRows(summary);
        var pageNumber = 1;
        var canvas = BeginPage(document, paints, companyName, summary, pageNumber);
        var y = TableTop;

        DrawTableHeader(canvas, paints, y);
        y += RowHeight;

        foreach (var row in rows)
        {
            if (y + RowHeight > PageHeight - Margin)
            {
                document.EndPage();
                pageNumber++;
                canvas = BeginPage(document, paints, companyName, summary, pageNumber);
                y = TableTop;
                DrawTableHeader(canvas, paints, y);
                y += RowHeight;
            }

            DrawTableRow(canvas, paints, y, row);
            y += RowHeight;
        }

        y += 14f;
        DrawNote(document, ref canvas, paints, companyName, summary, ref pageNumber, ref y, note);

        document.EndPage();
        document.Close();
    }

    private static SKCanvas BeginPage(
        SKDocument document,
        PdfPaintSet paints,
        string companyName,
        ReligiousReportSummary summary,
        int pageNumber)
    {
        var canvas = document.BeginPage(PageWidth, PageHeight);
        canvas.Clear(SKColors.White);

        canvas.DrawText("運営収支報告書", PageWidth / 2, HeaderTop, SKTextAlign.Center, paints.TitleFont, paints.Text);
        canvas.DrawLine(PageWidth / 2 - 70f, HeaderTop + 5f, PageWidth / 2 + 70f, HeaderTop + 5f, paints.Border);
        canvas.DrawText(
            $"自 {summary.PeriodStart:yyyy年M月d日}  至 {summary.PeriodEnd:yyyy年M月d日}",
            PageWidth / 2,
            HeaderTop + 25f,
            SKTextAlign.Center,
            paints.HeaderFont,
            paints.Text);
        canvas.DrawText(companyName, Margin, TableTop - 16f, SKTextAlign.Left, paints.HeaderFont, paints.Text);
        canvas.DrawText("(単位：円)", Margin + TableWidth, TableTop - 16f, SKTextAlign.Right, paints.HeaderFont, paints.Text);
        canvas.DrawText($"{pageNumber}", PageWidth / 2, PageHeight - 16f, SKTextAlign.Center, paints.SmallFont, paints.MutedText);

        return canvas;
    }

    private static List<PdfRow> BuildRows(ReligiousReportSummary summary)
    {
        var rows = new List<PdfRow>
        {
            PdfRow.Carryover("前期繰越収支差額", summary.PeriodOpeningCarryover),
            PdfRow.Section("収入の部"),
            PdfRow.Total("収入合計", summary.IncomeBudgetTotal, summary.IncomeActualTotal, summary.IncomeBudgetTotal - summary.IncomeActualTotal)
        };

        rows.AddRange(summary.Rows
            .Where(x => x.Kind == "income")
            .Select(PdfRow.Detail));

        rows.Add(PdfRow.Section("支出の部"));
        rows.Add(PdfRow.Total("支出合計", summary.ExpenseBudgetTotal, summary.ExpenseActualTotal, summary.ExpenseBudgetTotal - summary.ExpenseActualTotal));
        rows.AddRange(summary.Rows
            .Where(x => x.Kind == "expense")
            .Select(PdfRow.Detail));

        rows.Add(PdfRow.Total("当期収支差額", summary.NetBudget, summary.NetActual, summary.NetBudget - summary.NetActual, true));
        rows.Add(PdfRow.Carryover("次期繰越収支差額", summary.ClosingCarryover, true));

        return rows;
    }

    private static void DrawTableHeader(SKCanvas canvas, PdfPaintSet paints, float y)
    {
        using var fill = new SKPaint { Color = SKColor.Parse("#E6E9ED"), Style = SKPaintStyle.Fill };
        var x = Margin;
        canvas.DrawRect(new SKRect(x, y, x + TableWidth, y + RowHeight), fill);
        DrawTableGrid(canvas, paints, y, RowHeight);

        DrawText(canvas, "コード", x + 5f, y + 12.8f, paints.BoldFont, paints.Text);
        DrawText(canvas, "分類", x + CodeWidth + 5f, y + 12.8f, paints.BoldFont, paints.Text);
        DrawRight(canvas, "期間予算", x + CodeWidth + LabelWidth + AmountWidth - 5f, y + 12.8f, paints.BoldFont);
        DrawRight(canvas, "実績額", x + CodeWidth + LabelWidth + AmountWidth * 2 - 5f, y + 12.8f, paints.BoldFont);
        DrawRight(canvas, "差異", x + TableWidth - 5f, y + 12.8f, paints.BoldFont);
    }

    private static void DrawTableRow(SKCanvas canvas, PdfPaintSet paints, float y, PdfRow row)
    {
        var x = Margin;
        using var fill = new SKPaint
        {
            Color = row.Kind switch
            {
                PdfRowKind.Section => SKColor.Parse("#F1F5F9"),
                PdfRowKind.Total => row.IsFinal ? SKColor.Parse("#FFF7ED") : SKColor.Parse("#EEF4FB"),
                PdfRowKind.Carryover => row.IsFinal ? SKColor.Parse("#ECFDF3") : SKColor.Parse("#F8FAFC"),
                _ => SKColors.White
            },
            Style = SKPaintStyle.Fill
        };

        canvas.DrawRect(new SKRect(x, y, x + TableWidth, y + RowHeight), fill);
        DrawTableGrid(canvas, paints, y, RowHeight);

        var font = row.Kind == PdfRowKind.Detail ? paints.BodyFont : paints.BoldFont;
        if (row.Kind == PdfRowKind.Section)
        {
            DrawText(canvas, row.Label, x + 5f, y + 12.8f, font, paints.Text);
            return;
        }

        DrawText(canvas, row.Code, x + 5f, y + 12.8f, font, paints.Text);
        DrawText(canvas, row.Label, x + CodeWidth + 5f, y + 12.8f, font, paints.Text);

        if (row.Budget.HasValue)
        {
            DrawRight(canvas, FormatAmount(row.Budget.Value), x + CodeWidth + LabelWidth + AmountWidth - 5f, y + 12.8f, font);
        }

        if (row.Actual.HasValue)
        {
            DrawRight(canvas, FormatAmount(row.Actual.Value), x + CodeWidth + LabelWidth + AmountWidth * 2 - 5f, y + 12.8f, font);
        }

        if (row.Variance.HasValue)
        {
            DrawRight(canvas, FormatAmount(row.Variance.Value), x + TableWidth - 5f, y + 12.8f, font);
        }
    }

    private static void DrawNote(
        SKDocument document,
        ref SKCanvas canvas,
        PdfPaintSet paints,
        string companyName,
        ReligiousReportSummary summary,
        ref int pageNumber,
        ref float y,
        string note)
    {
        var lines = WrapText(string.IsNullOrWhiteSpace(note) ? " " : note.Trim(), paints.BodyFont, TableWidth - 18f);
        var requiredHeight = 28f + Math.Max(3, lines.Count) * NoteLineHeight;
        if (y + requiredHeight > PageHeight - Margin)
        {
            document.EndPage();
            pageNumber++;
            canvas = BeginPage(document, paints, companyName, summary, pageNumber);
            y = TableTop;
        }

        using var fill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        var height = requiredHeight;
        canvas.DrawRect(new SKRect(Margin, y, Margin + TableWidth, y + height), fill);
        canvas.DrawRect(new SKRect(Margin, y, Margin + TableWidth, y + height), paints.Border);
        DrawText(canvas, "注記", Margin + 8f, y + 16f, paints.BoldFont, paints.Text);

        var lineY = y + 34f;
        foreach (var line in lines.DefaultIfEmpty(" "))
        {
            DrawText(canvas, line, Margin + 8f, lineY, paints.BodyFont, paints.Text);
            lineY += NoteLineHeight;
        }
    }

    private static void DrawTableGrid(SKCanvas canvas, PdfPaintSet paints, float y, float height)
    {
        var x = Margin;
        canvas.DrawRect(new SKRect(x, y, x + TableWidth, y + height), paints.Border);
        canvas.DrawLine(x + CodeWidth, y, x + CodeWidth, y + height, paints.Border);
        canvas.DrawLine(x + CodeWidth + LabelWidth, y, x + CodeWidth + LabelWidth, y + height, paints.Border);
        canvas.DrawLine(x + CodeWidth + LabelWidth + AmountWidth, y, x + CodeWidth + LabelWidth + AmountWidth, y + height, paints.Border);
        canvas.DrawLine(x + CodeWidth + LabelWidth + AmountWidth * 2, y, x + CodeWidth + LabelWidth + AmountWidth * 2, y + height, paints.Border);
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                lines.Add("");
                continue;
            }

            var current = "";
            foreach (var rune in paragraph.EnumerateRunes())
            {
                var next = current + rune;
                if (current.Length > 0 && font.MeasureText(next) > maxWidth)
                {
                    lines.Add(current);
                    current = rune.ToString();
                }
                else
                {
                    current = next;
                }
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }
        }

        return lines;
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
    {
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }

    private static void DrawRight(SKCanvas canvas, string text, float rightX, float y, SKFont font)
    {
        using var paint = new SKPaint { Color = SKColor.Parse("#111827"), IsAntialias = true };
        var width = font.MeasureText(text, paint);
        canvas.DrawText(text, rightX - width, y, SKTextAlign.Left, font, paint);
    }

    private static string FormatAmount(decimal amount)
    {
        return amount < 0 ? $"△{Math.Abs(amount):N0}" : amount.ToString("N0");
    }

    private sealed class PdfPaintSet : IDisposable
    {
        public PdfPaintSet(SKTypeface typeface)
        {
            Typeface = typeface;
            Text = new SKPaint { Color = SKColor.Parse("#111827"), IsAntialias = true };
            MutedText = new SKPaint { Color = SKColor.Parse("#64748B"), IsAntialias = true };
            Border = new SKPaint { Color = SKColor.Parse("#111827"), Style = SKPaintStyle.Stroke, StrokeWidth = 0.7f, IsAntialias = false };
            BodyFont = new SKFont(typeface, BodyFontSize);
            BoldFont = new SKFont(typeface, BodyFontSize) { Embolden = true };
            HeaderFont = new SKFont(typeface, HeaderFontSize);
            TitleFont = new SKFont(typeface, TitleFontSize) { Embolden = true };
            SmallFont = new SKFont(typeface, 8f);
        }

        public SKTypeface Typeface { get; }
        public SKPaint Text { get; }
        public SKPaint MutedText { get; }
        public SKPaint Border { get; }
        public SKFont BodyFont { get; }
        public SKFont BoldFont { get; }
        public SKFont HeaderFont { get; }
        public SKFont TitleFont { get; }
        public SKFont SmallFont { get; }

        public void Dispose()
        {
            SmallFont.Dispose();
            TitleFont.Dispose();
            HeaderFont.Dispose();
            BoldFont.Dispose();
            BodyFont.Dispose();
            Border.Dispose();
            MutedText.Dispose();
            Text.Dispose();
        }
    }

    private sealed record PdfRow(
        string Code,
        string Label,
        decimal? Budget,
        decimal? Actual,
        decimal? Variance,
        PdfRowKind Kind,
        bool IsFinal = false)
    {
        public static PdfRow Detail(ReligiousReportRow row) => new(row.CategoryCode, row.CategoryName, row.BudgetAmount, row.ActualAmount, row.VarianceAmount, PdfRowKind.Detail);
        public static PdfRow Section(string label) => new("", label, null, null, null, PdfRowKind.Section);
        public static PdfRow Total(string label, decimal budget, decimal actual, decimal variance, bool isFinal = false) => new("", label, budget, actual, variance, PdfRowKind.Total, isFinal);
        public static PdfRow Carryover(string label, decimal actual, bool isFinal = false) => new("", label, null, actual, null, PdfRowKind.Carryover, isFinal);
    }

    private enum PdfRowKind
    {
        Detail,
        Section,
        Total,
        Carryover
    }
}
