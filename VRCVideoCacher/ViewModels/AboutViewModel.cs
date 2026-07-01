using Jeek.Avalonia.Localization;

namespace VRCVideoCacher.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public string Version => Program.Version;
    public string CreatedBy => Localizer.Get("CreatedBy") + $" {Program.CreatorElly}, {Program.CreatorNatsumi}, {Program.CreatorHaxy}, {Program.CreatorHauskaz}, {Program.CreatorDubyaDude}";
}
