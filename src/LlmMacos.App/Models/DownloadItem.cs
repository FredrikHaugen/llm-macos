using CommunityToolkit.Mvvm.ComponentModel;
using LlmMacos.Core.Models;

namespace LlmMacos.App.Models;

public partial class DownloadItem : ObservableObject
{
    [ObservableProperty]
    private string downloadId = string.Empty;

    [ObservableProperty]
    private string label = string.Empty;

    [ObservableProperty]
    private DownloadStatus status;

    [ObservableProperty]
    private long bytesDownloaded;

    [ObservableProperty]
    private long totalBytes;

    [ObservableProperty]
    private double percent;

    [ObservableProperty]
    private string message = string.Empty;
}
