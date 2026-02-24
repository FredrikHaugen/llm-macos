using CommunityToolkit.Mvvm.ComponentModel;
using LlmMacos.Core.Models;

namespace LlmMacos.App.Models;

public partial class DownloadItem : ObservableObject
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    [ObservableProperty]
    private string downloadId = string.Empty;

    [ObservableProperty]
    private string label = string.Empty;

    [ObservableProperty]
    private DownloadStatus status;

    [ObservableProperty]
    private long bytesDownloaded;

    [ObservableProperty]
    private long? totalBytes;

    [ObservableProperty]
    private double? percent;

    [ObservableProperty]
    private double? bytesPerSecond;

    [ObservableProperty]
    private TimeSpan? eta;

    [ObservableProperty]
    private bool isIndeterminate;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    private string speedText = string.Empty;

    [ObservableProperty]
    private string etaText = string.Empty;

    public static string FormatBytes(double bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var value = bytes;
        var order = 0;
        while (value >= 1024 && order < SizeUnits.Length - 1)
        {
            order++;
            value /= 1024;
        }

        return $"{value:0.##} {SizeUnits[order]}";
    }
}
