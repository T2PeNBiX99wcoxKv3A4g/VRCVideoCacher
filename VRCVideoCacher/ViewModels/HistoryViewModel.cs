using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.ViewModels;

public partial class HistoryItemViewModel : ViewModelBase
{
    public int Key { get; init; }
    public DateTime Timestamp { get; init; }
    public string Url { get; init; }
    public string? Id { get; init; }
    public UrlType Type { get; init; }
    public string? Author { get; init; }
    public bool HasAuthor => !string.IsNullOrEmpty(Author);

    private string? _title;

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrEmpty(_title)) return _title;
            return Url.Length > 60 ? Url[..57] + "..." : Url;
        }
    }

    public string TypeBadge => Type switch
    {
        UrlType.YouTube => "YouTube",
        UrlType.PyPyDance => "PyPyDance",
        UrlType.VRDancing => "VRDancing",
        _ => "Other"
    };

    public IBrush TypeBadgeColor => Type switch
    {
        UrlType.YouTube => new SolidColorBrush(Color.Parse("#CC0000")),
        UrlType.PyPyDance => new SolidColorBrush(Color.Parse("#4A90D9")),
        UrlType.VRDancing => new SolidColorBrush(Color.Parse("#7B68EE")),
        _ => new SolidColorBrush(Color.Parse("#555555"))
    };

    public string? ThumbnailUrl { get; private set; }

    public HistoryItemViewModel(History history, VideoInfoCache? meta)
    {
        Key = history.Key;
        Timestamp = history.Timestamp.ToLocalTime();
        Url = history.Url;
        Id = history.Id;
        Type = history.Type;
        _title = meta?.Title;
        Author = meta?.Author;
    }

    public void SetMetadata(string? title, string? thumbnailUrl)
    {
        if (!string.IsNullOrEmpty(title))
        {
            _title = title;
            OnPropertyChanged(nameof(DisplayTitle));
        }

        if (!string.IsNullOrEmpty(thumbnailUrl))
        {
            ThumbnailUrl = thumbnailUrl;
            OnPropertyChanged(nameof(ThumbnailUrl));
        }
    }

    public async Task<(string? DisplayTitle, string? ThumbnailUrl)> LoadMetadataAsync()
    {
        if (Id != null)
        {
            // Load from DB
            var videoInfo = await YouTubeMetadataService.GetVideoMetadataAsync(Id);

            if (!string.IsNullOrEmpty(videoInfo?.Title))
            {
                _title = videoInfo.Title;
                OnPropertyChanged(nameof(DisplayTitle));
            }

            // Load thumbnail
            var thumbnailPath = ThumbnailManager.GetThumbnail(Id);
            if (Id.Length == 11 && string.IsNullOrEmpty(thumbnailPath))
                thumbnailPath = await YouTubeMetadataService.GetThumbnail(Id);

            if (!string.IsNullOrEmpty(thumbnailPath))
            {
                ThumbnailUrl = thumbnailPath;
                OnPropertyChanged(nameof(ThumbnailUrl));
            }
        }

        return (DisplayTitle, ThumbnailUrl);
    }

    [RelayCommand]
    private void OpenUrl()
    {
        Try.Run(() => Process.Start(new ProcessStartInfo
        {
            FileName = Url,
            UseShellExecute = true
        }));
    }

    [RelayCommand]
    private async Task CopyUrl()
    {
        if (Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(Url);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        // Removes the video from history entirely (all of its play records), so it doesn't just reappear
        // from an earlier play. DatabaseManager raises OnPlayHistoryChanged, which the list refreshes on.
        DatabaseManager.DeletePlayHistoryForVideo(Id, Url);
    }
}

public partial class HistoryViewModel : ViewModelBase
{
    [ObservableProperty] public partial string SearchFilter { get; set; } = string.Empty;

    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty] public partial int MaxSize { get; set; } = 1000;
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];
    public ObservableCollection<HistoryItemViewModel> FilteredHistoryItems { get; } = [];

    public HistoryViewModel()
    {
        DatabaseManager.OnPlayHistoryAdded += () => Dispatcher.UIThread.Post(Refresh);
        DatabaseManager.OnPlayHistoryChanged += () => Dispatcher.UIThread.Post(Refresh);
        ConfigManager.OnConfigChanged += LoadFromConfig;
        LoadFromConfig();

        // NB: deliberately NOT subscribing to OnVideoInfoCacheUpdated. The background metadata load writes
        // to VideoInfoCache, which raises that event — refreshing on it looped endlessly, and the old fix
        // (a guard that dropped refreshes while metadata loaded) also swallowed delete-triggered refreshes.
        // Metadata is applied to the rows in place by LoadMetadata, so a full reload here isn't needed.

        Dispatcher.UIThread.Post(Refresh);
    }

    private void LoadFromConfig()
    {
        var config = ConfigManager.Config;
        MaxSize = config.HistoryMaxSize;
    }

    partial void OnMaxSizeChanged(int value)
    {
        if (value <= 0)
            return;

        ConfigManager.Config.HistoryMaxSize = value;
        ConfigManager.TrySaveConfigWithoutWait(false);
        // Apply the new cap immediately (trims the DB if it was lowered), then reload the list.
        DatabaseManager.TrimPlayHistory(value);
        Dispatcher.UIThread.Post(Refresh);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredHistoryItems.Clear();

        var filter = SearchFilter.ToLowerInvariant().Trim();
        foreach (var item in HistoryItems)
            if (string.IsNullOrEmpty(filter) ||
                !string.IsNullOrEmpty(item.DisplayTitle) && item.DisplayTitle.ToLowerInvariant()
                    .Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(item.Id) &&
                item.Id.ToLowerInvariant().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(item.Url) &&
                item.Url.ToLowerInvariant().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(item.Author) &&
                item.Author.ToLowerInvariant().Contains(filter, StringComparison.OrdinalIgnoreCase))
                FilteredHistoryItems.Add(item);

        StatusText = string.IsNullOrEmpty(filter)
            ? string.Format(Localizer.Get("EntriesCountFormat"), HistoryItems.Count)
            : string.Format(Localizer.Get("VideosCountFormat"), FilteredHistoryItems.Count, HistoryItems.Count);
    }

    private bool _isLoadingMetadata;
    private readonly List<HistoryItemViewModel> _pendingMetadata = [];

    [RelayCommand]
    private void Refresh()
    {
        var fresh = DatabaseManager
            .GetVideoHistoryAsCache(ConfigManager.Config.HistoryMaxSize, true)
            .OrderByDescending(h => h.Timestamp)
            .ToList();

        // Reuse the existing row VMs (keyed by the play record's PK) so their already-loaded thumbnails and
        // commands survive. Only rows genuinely new to the list are freshly created — so a delete just drops
        // one row, leaving every other image and delete button untouched (no flicker, no stale click target).
        var existing = HistoryItems.ToDictionary(i => i.Key);
        var desired = new List<HistoryItemViewModel>(fresh.Count);
        var newItems = new List<HistoryItemViewModel>();
        foreach (var item in fresh)
            if (existing.TryGetValue(item.Key, out var reused))
                desired.Add(reused);
            else
            {
                desired.Add(item);
                newItems.Add(item);
            }

        // Apply as an in-place diff rather than Clear()+re-add, so unchanged containers are never torn down.
        var desiredKeys = new HashSet<int>(desired.Select(d => d.Key));
        for (var i = HistoryItems.Count - 1; i >= 0; i--)
            if (!desiredKeys.Contains(HistoryItems[i].Key))
                HistoryItems.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < HistoryItems.Count && ReferenceEquals(HistoryItems[i], desired[i]))
                continue;

            var found = -1;
            for (var j = i + 1; j < HistoryItems.Count; j++)
                if (ReferenceEquals(HistoryItems[j], desired[i]))
                {
                    found = j;
                    break;
                }

            if (found >= 0)
                HistoryItems.Move(found, i);
            else
                HistoryItems.Insert(i, desired[i]);
        }

        ApplyFilter();

        // Only genuinely-new rows need their titles/thumbnails fetched.
        QueueMetadata(newItems);
    }

    private void QueueMetadata(List<HistoryItemViewModel> items)
    {
        if (items.Count == 0)
            return;
        if (_isLoadingMetadata)
        {
            _pendingMetadata.AddRange(items);
            return;
        }

        LoadMetadata(items);
    }

    private void LoadMetadata(List<HistoryItemViewModel> items)
    {
        _isLoadingMetadata = true;

        _ = Task.Run(async () =>
        {
            foreach (var groupedItems in items.GroupBy(h => h.Id))
            {
                (string? DisplayTitle, string? ThumbnailUrl)? metadata = null;
                foreach (var item in groupedItems)
                    if (metadata == null)
                        metadata = await item.LoadMetadataAsync();
                    else
                        item.SetMetadata(metadata.Value.DisplayTitle, metadata.Value.ThumbnailUrl);
            }

            Dispatcher.UIThread.Post(() =>
            {
                _isLoadingMetadata = false;
                if (!string.IsNullOrWhiteSpace(SearchFilter))
                    ApplyFilter();
                if (_pendingMetadata.Count > 0)
                {
                    var next = _pendingMetadata.ToList();
                    _pendingMetadata.Clear();
                    LoadMetadata(next);
                }
            });
        });
    }

    [RelayCommand]
    private static async Task ClearAll()
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var confirmed = await ConfirmWindow.ShowAsync(
            desktop.MainWindow!,
            Localizer.Get("ClearAllHistory"),
            Localizer.Get("ClearAllHistoryConfirm"));
        if (confirmed)
            DatabaseManager.ClearPlayHistory();
    }
}