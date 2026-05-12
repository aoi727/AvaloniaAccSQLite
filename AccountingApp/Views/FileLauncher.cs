using System.Diagnostics;

namespace AccountingApp.Views;

internal static class FileLauncher
{
    public static string? Open(string path, string label = "ファイル")
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
            return $"{label}を開けませんでした: {ex.Message}";
        }
    }
}
