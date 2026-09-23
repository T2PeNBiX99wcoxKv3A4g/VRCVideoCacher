using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.ViewModels;

public partial class CacheItemViewModel : ViewModelBase
{
    public string FileName { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string Extension { get; init; } = string.Empty;
    public UrlType Type { get; set; } = UrlType.Other;

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;

    [ObservableProperty] public partial string ThumbnailSource { get; set; } = string.Empty;

    public string DisplayTitle => string.IsNullOrEmpty(Title) ? VideoId : Title;

    public string SizeFormatted => FormatSize(Size);

    // Event to notify parent when item is deleted
    public event Action<CacheItemViewModel>? OnDeleted;

    public async Task LoadMetadataAsync()
    {
        // Load from DB
        var videoInfo = await YouTubeMetadataService.GetVideoMetadataAsync(VideoId);

        if (videoInfo != null)
        {
            Type = videoInfo.Type;
            if (!string.IsNullOrEmpty(videoInfo.Title))
            {
                Title = videoInfo.Title;
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
        else
        {
            if (VideoId.StartsWith("sm", StringComparison.OrdinalIgnoreCase) ||
                VideoId.StartsWith("so", StringComparison.OrdinalIgnoreCase) ||
                VideoId.StartsWith("nm", StringComparison.OrdinalIgnoreCase))
                Type = UrlType.NicoVideo;
            else if (VideoId.Length == 11)
                Type = UrlType.YouTube;
        }

        // Load thumbnail
        var thumbnailPath = ThumbnailManager.GetThumbnail(VideoId);
        if (VideoId.Length == 11 && string.IsNullOrEmpty(thumbnailPath))
            thumbnailPath = await YouTubeMetadataService.GetThumbnail(VideoId);

        if (!string.IsNullOrEmpty(thumbnailPath))
            ThumbnailSource = thumbnailPath;
    }

    public string GetWebUrl()
    {
        if (VideoId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            VideoId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return VideoId;

        return Type switch
        {
            UrlType.YouTube => $"https://www.youtube.com/watch?v={VideoId}",
            UrlType.NicoVideo => $"https://www.nicovideo.jp/watch/{VideoId}",
            _ => $"https://www.youtube.com/watch?v={VideoId}"
        };
    }

    [RelayCommand]
    private void OpenUrl()
    {
        var url = GetWebUrl();
        Try.Run(() =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        });
    }

    [RelayCommand]
    private void OpenOnYouTube() => OpenUrl();

    [RelayCommand]
    private async Task CopyUrl()
    {
        var url = $"{ConfigManager.Config.YtdlpWebServerUrl}/{FileName}";
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(url);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        CacheManager.DeleteCacheItem(FileName);
        OnDeleted?.Invoke(this);
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        if (bytes == 0) return "0 B";
        var mag = (int)Math.Log(bytes, 1024);
        mag = Math.Min(mag, suffixes.Length - 1);
        var adjustedSize = bytes / Math.Pow(1024, mag);
        return $"{adjustedSize:N2} {suffixes[mag]}";
    }
}

public partial class CacheBrowserViewModel : ViewModelBase
{
    [ObservableProperty] public partial string SearchFilter { get; set; } = string.Empty;

    [ObservableProperty] public partial CacheItemViewModel? SelectedItem { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    public ObservableCollection<CacheItemViewModel> CachedVideos { get; } = [];
    public ObservableCollection<CacheItemViewModel> FilteredVideos { get; } = [];

    public CacheBrowserViewModel() => CacheManager.OnCacheChanged += OnCacheChanged;

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCache);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredVideos.Clear();

        var filter = SearchFilter.ToLowerInvariant().Trim();
        foreach (var video in CachedVideos)
            if (string.IsNullOrEmpty(filter) ||
                video.FileName.ToLowerInvariant().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                video.VideoId.ToLowerInvariant().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(video.Title) &&
                video.Title.ToLowerInvariant().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(video.DisplayTitle) && video.DisplayTitle.ToLowerInvariant()
                    .Contains(filter, StringComparison.OrdinalIgnoreCase))
                FilteredVideos.Add(video);

        StatusText = string.Format(Localizer.Get("VideosCountFormat"), FilteredVideos.Count, CachedVideos.Count);
    }

    [RelayCommand]
    private void RefreshCache()
    {
        CachedVideos.Clear();
        FilteredVideos.Clear();

        var cachedAssets = CacheManager.GetCachedAssets();
        var itemsToLoad = new List<CacheItemViewModel>();

        foreach (var (fileName, cache) in cachedAssets.OrderByDescending(x => x.Value.LastModified))
        {
            // Filter out non-video files like index.html
            if (fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                continue;

            var videoId = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);

            var item = new CacheItemViewModel
            {
                FileName = fileName,
                VideoId = videoId,
                Size = cache.Size,
                LastModified = cache.LastModified,
                Extension = extension
            };

            // Subscribe to delete event
            item.OnDeleted += OnItemDeleted;

            CachedVideos.Add(item);
            itemsToLoad.Add(item);
        }

        ApplyFilter();

        // Load metadata (titles + thumbnails) asynchronously in the background
        _ = Task.Run(async () =>
        {
            var updatedTitle = false;
            foreach (var item in itemsToLoad)
            {
                var prevTitle = item.Title;
                await item.LoadMetadataAsync();
                if (item.Title != prevTitle)
                    updatedTitle = true;
            }

            if (updatedTitle && !string.IsNullOrWhiteSpace(SearchFilter))
                _ = Dispatcher.UIThread.InvokeAsync(ApplyFilter);
        });
    }

    private void OnItemDeleted(CacheItemViewModel item)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CachedVideos.Remove(item);
            FilteredVideos.Remove(item);
            StatusText = string.Format(Localizer.Get("VideosCountFormat"), FilteredVideos.Count, CachedVideos.Count);
        });
    }

    [RelayCommand]
    private static async Task DeleteAll()
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var confirmed = await ConfirmWindow.ShowAsync(
            desktop.MainWindow!,
            Localizer.Get("DeleteAllCache"),
            Localizer.Get("DeleteAllCacheConfirm"));
        if (confirmed)
            await CacheManager.ClearCache();
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            if (SelectedItem != null)
            {
                var filePath = Path.Join(cachePath, SelectedItem.FileName);
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
                Process.Start("explorer.exe", cachePath);
        }
        else if (OperatingSystem.IsLinux())
            Process.Start("xdg-open", cachePath);
    }
}