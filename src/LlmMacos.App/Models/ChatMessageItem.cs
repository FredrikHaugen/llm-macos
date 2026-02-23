using CommunityToolkit.Mvvm.ComponentModel;

namespace LlmMacos.App.Models;

public partial class ChatMessageItem : ObservableObject
{
    [ObservableProperty]
    private string role = string.Empty;

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private DateTimeOffset createdAt;
}
