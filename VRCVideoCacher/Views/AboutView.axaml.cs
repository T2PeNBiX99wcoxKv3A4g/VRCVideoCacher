using Avalonia.Controls;
using Avalonia.Interactivity;
using VRCVideoCacher.Utils;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class AboutView : UserControl
{
    private const string GithubUrl = "https://github.com/EllyVR/VRCVideoCacher";
    private const string DiscordUrl = "https://discord.gg/z5kVNkmQuS";
    private const string SteamUrl = "https://store.steampowered.com/app/4296960/";

    public AboutView()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }

    private void OnDiscordClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl.Open(DiscordUrl);
    }

    private void OnGitHubClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl.Open(GithubUrl);
    }

    private void OnSteamClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl.Open(SteamUrl);
    }

    private void OnGitHubIssueClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl.Open($"{GithubUrl}/issues");
    }

    private void OnDiscordIssueClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl.Open(DiscordUrl);
    }
}