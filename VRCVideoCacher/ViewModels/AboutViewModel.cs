using Jeek.Avalonia.Localization;

namespace VRCVideoCacher.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public string Version { get; } = Program.Version;
    public string CreatedBy { get; } = Localizer.Get("CreatedBy") + $" {Program.Creator_Elly}, {Program.Creator_Natsumi}, {Program.Creator_Haxy}, {Program.Creator_Hauskaz}, {Program.Creator_DubyaDude}";
}
