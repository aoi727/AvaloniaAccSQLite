using Avalonia.Controls;
using Avalonia.Platform;

namespace AccountingApp;

internal static class AppIconProvider
{
    private static readonly Uri IconUri = new("avares://AccountingApp/Assets/app-icon.png");

    public static WindowIcon CreateWindowIcon()
    {
        return new WindowIcon(AssetLoader.Open(IconUri));
    }
}
