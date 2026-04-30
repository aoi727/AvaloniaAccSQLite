using System.Diagnostics;

namespace AccountingApp.Views;

internal static class PdfPreviewLauncher
{
    public static string? Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return null;
        }
        catch (Exception ex)
        {
            return $"PDFは保存しましたが、自動で開けませんでした: {ex.Message}";
        }
    }
}
