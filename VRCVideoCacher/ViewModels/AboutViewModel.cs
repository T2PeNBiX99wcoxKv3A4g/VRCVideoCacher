using Jeek.Avalonia.Localization;

namespace VRCVideoCacher.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public string Version { get; }
    public string CreatedBy { get; }

    public AboutViewModel()
    {
        Version = Program.Version;
        CreatedBy = Localizer.Get("CreatedBy") + $" {Program.Creator_Elly}, {Program.Creator_Natsumi}, {Program.Creator_Haxy}, {Program.Creator_Hauskaz}, {Program.Creator_DubyaDude}";
    }
}
