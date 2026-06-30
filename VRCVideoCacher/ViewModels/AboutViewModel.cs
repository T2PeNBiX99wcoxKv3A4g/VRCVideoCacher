using Jeek.Avalonia.Localization;

namespace VRCVideoCacher.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public string Version { get; } = Program.Version;
    public string CreatedBy { get; } = Localizer.Get("CreatedBy") + $" {Program.CreatorElly}, {Program.CreatorNatsumi}, {Program.CreatorHaxy}, {Program.CreatorHauskaz}, {Program.CreatorDubyaDude}";
}
