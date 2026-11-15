using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using JetBrains.Annotations;
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
    [PublicAPI] public UrlType Type { get; set; } = UrlType.Other;

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;

    [ObservableProperty] public partial string ThumbnailSource { get; set; } = string.Empty;

    public string DisplayTitle => string.IsNullOrEmpty(Title) ? VideoId : Title;

    public string SizeFormatted => FormatSize(Size);

    // Event to notify parent when item is deleted
    public event Action<CacheItemViewModel>? OnDeleted;

    public async Task LoadMetadataAsync()
    {
        if (NicoVideoApiService.Instance.IsValidVideoId(VideoId))
            Type = UrlType.NicoVideo;
        else if (VideoId.Length == 11)
            Type = UrlType.YouTube;

        // Load from DB
        var videoInfo = Type switch
        {
            UrlType.YouTube => await YouTubeMetadataService.GetVideoMetadataAsync(VideoId),
            UrlType.NicoVideo => await NicoVideoApiService.Instance.GetVideoMetadataAsync(VideoId),
            _ => null
        };

        if (videoInfo != null)
        {
            Type = videoInfo.Type;
            if (!string.IsNullOrEmpty(videoInfo.Title))
            {
                Title = videoInfo.Title;
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        // Load thumbnail
        var thumbnailPath = ThumbnailManager.GetThumbnail(VideoId);
        if (string.IsNullOrEmpty(thumbnailPath))
            thumbnailPath = Type switch
            {
                UrlType.YouTube => await YouTubeMetadataService.GetThumbnail(VideoId),
                UrlType.NicoVideo => await NicoVideoApiService.Instance.GetThumbnail(VideoId),
                _ => null
            };

        if (!string.IsNullOrEmpty(thumbnailPath))
            ThumbnailSource = thumbnailPath;
    }

    private string GetWebUrl()
    {
        if (VideoId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            VideoId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return VideoId;

        return Type switch
        {
            UrlType.YouTube => $"https://www.youtube.com/watch?v={VideoId}",
            UrlType.NicoVideo => NicoVideoApiService.Instance.IsValidLiveId(VideoId)
                ? $"https://live.nicovideo.jp/watch/{VideoId}"
                : $"https://www.nicovideo.jp/watch/{VideoId}",
            _ => $"{ConfigManager.Instance.Config.YtdlpWebServerUrl}/{VideoId}.mp4"
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
        var url = $"{ConfigManager.Instance.Config.YtdlpWebServerUrl}/{FileName}";
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
        CacheManager.Instance.DeleteCacheItem(FileName);
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
        Dispatcher.UIThread.Post(() => HandleCacheChange(fileName, changeType));
    }

    private void HandleCacheChange(string fileName, CacheChangeType changeType)
    {
        switch (changeType)
        {
            case CacheChangeType.Added:
            {
                var cache = CacheManager.Instance.GetCachedAsset(fileName);
                if (cache == null || CachedVideos.Any(v => v.FileName == fileName))
                    return;

                var item = CreateItem(fileName, cache);
                InsertItem(item);
                ApplyFilter();
                _ = LoadMetadataAsync([item]);
                break;
            }
            case CacheChangeType.Removed:
            {
                var item = CachedVideos.FirstOrDefault(v => v.FileName == fileName);
                if (item != null && CachedVideos.Remove(item))
                    ApplyFilter();
                break;
            }
            case CacheChangeType.Cleared:
                CachedVideos.Clear();
                FilteredVideos.Clear();
                StatusText = string.Format(Localizer.Get("VideosCountFormat"), 0, 0);
                break;
        }
    }

    private CacheItemViewModel CreateItem(string fileName, VideoCache cache)
    {
        var item = new CacheItemViewModel
        {
            FileName = fileName,
            VideoId = Path.GetFileNameWithoutExtension(fileName),
            Size = cache.Size,
            LastModified = cache.LastModified,
            Extension = Path.GetExtension(fileName)
        };
        item.OnDeleted += OnItemDeleted;
        return item;
    }

    private void InsertItem(CacheItemViewModel item)
    {
        var index = 0;
        while (index < CachedVideos.Count && CachedVideos[index].LastModified > item.LastModified)
            index++;
        CachedVideos.Insert(index, item);
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

        var itemsToLoad = new List<CacheItemViewModel>();

        foreach (var (fileName, cache) in CacheManager.Instance.GetCachedAssets()
                     .OrderByDescending(x => x.Value.LastModified))
        {
            // Filter out non-video files like index.html
            if (fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                continue;

            var item = CreateItem(fileName, cache);
            CachedVideos.Add(item);
            itemsToLoad.Add(item);
        }

        ApplyFilter();

        if (itemsToLoad.Count > 0)
            _ = LoadMetadataAsync(itemsToLoad);
    }

    /// <summary>Loads titles and thumbnails with a small bounded fan-out instead of serially.</summary>
    private async Task LoadMetadataAsync(IReadOnlyCollection<CacheItemViewModel> items)
    {
        var updatedTitle = 0;

        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (item, _) =>
            {
                var previousTitle = item.Title;
                await item.LoadMetadataAsync();
                if (item.Title != previousTitle)
                    Interlocked.Exchange(ref updatedTitle, 1);
            });

        if (updatedTitle != 0 && !string.IsNullOrWhiteSpace(SearchFilter))
            await Dispatcher.UIThread.InvokeAsync(ApplyFilter);
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
            await CacheManager.Instance.ClearCache();
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        var cachePath = CacheManager.Instance.CachePath;
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