#if DEBUG
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.ViewModels;

public partial class DebugViewModel : ViewModelBase
{
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Ready.";

    [RelayCommand]
    private void SendTestError()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        Program.Logger.Error("Debug test error {Id}", id);
        StatusMessage = $"Logged test error {id}.";
    }

    [RelayCommand]
    private void SendTestWarning()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        Program.Logger.Warning("Debug test warning {Id}", id);
        StatusMessage = $"Logged test warning {id}.";
    }

    [RelayCommand]
    private void ShowTestToast()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        NotificationService.ShowNotification("VRCVideoCacher Debug", $"Direct toast test {id}");
        StatusMessage = $"Requested toast {id}.";
    }
}
#endif
