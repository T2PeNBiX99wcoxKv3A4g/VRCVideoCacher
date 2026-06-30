using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        OpenUrl(DiscordUrl);
    }

    private void OnGitHubClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl(GithubUrl);
    }

    private void OnSteamClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl(SteamUrl);
    }

    private void OnGitHubIssueClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl($"{GithubUrl}/issues");
    }

    private void OnDiscordIssueClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl(DiscordUrl);
    }

    private void OpenUrl(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch { /* Optionally handle errors */ }
    }
}
