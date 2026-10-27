using System.Diagnostics;
using JetBrains.Annotations;
using SkiaSharp;
using Valve.VR;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services;

public partial class VROverlayService : Singleton<VROverlayService>
{
    private const string DashboardKey = "com.github.ellyvr.vrcvideocacher.dashboard";
    private const string FloatingKey = "com.github.ellyvr.vrcvideocacher.floating";

    // In-VR notification placement: head-locked, centred and low so it reads like a subtitle. At the
    // default 0.5 m width the 4:1 strip is only ~12 cm tall, leaving the game view almost untouched.
    private const float NotificationVerticalOffset = -0.30f;
    private const float NotificationPitch = 10f;

    private const int UpdateIntervalMs = 100;
    private const int DashboardRenderIntervalMs = 1000;

    // Keep the strip up briefly after the last download finishes so it doesn't blink between queue items.
    private static readonly TimeSpan HideGrace = TimeSpan.FromSeconds(2);

    private ulong _dashboardOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
    private ulong _dashboardThumbnailHandle = OpenVR.k_ulOverlayHandleInvalid;
    private ulong _floatingOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;

    private CancellationTokenSource? _updateLoopCts;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _floatingShown;
    private string? _floatingKey;
    private DateTime _lastActivityUtc = DateTime.MinValue;
    private long _lastDashboardRenderTicks;

    private static CVROverlay? Overlay => (CVROverlay?)OpenVR.Overlay;

    [PublicAPI]
    public void Start2()
    {
        if (Overlay == null)
        {
            Log.Warning("OpenVR Overlay interface is not available");
            return;
        }

        InitializeOverlays();

        _updateLoopCts?.Cancel();
        _updateLoopCts?.TryDispose();
        _updateLoopCts = new();

        _ = Task.Run(() => UpdateLoop(_updateLoopCts.Token));

        ConfigManager.OnConfigChanged += OnConfigChanged;
    }

    [PublicAPI]
    public void Stop2()
    {
        ConfigManager.OnConfigChanged -= OnConfigChanged;

        _updateLoopCts?.Cancel();
        _updateLoopCts?.TryDispose();
        _updateLoopCts = null;

        DestroyOverlays();
    }

    private void OnConfigChanged()
    {
        var config = ConfigManager.Config;
        if (!config.VrOverlayEnabled)
        {
            DestroyOverlays();
            return;
        }

        InitializeOverlays();
    }

    private void InitializeOverlays()
    {
        var config = ConfigManager.Config;
        if (!config.VrOverlayEnabled || Overlay == null)
            return;

        // 1. Dashboard Overlay — keeps the full status panel.
        if (config.VrDashboardOverlayEnabled && _dashboardOverlayHandle == OpenVR.k_ulOverlayHandleInvalid)
        {
            var err = OpenVR.Overlay.CreateDashboardOverlay(
                DashboardKey,
                "VRC Video Cacher",
                ref _dashboardOverlayHandle,
                ref _dashboardThumbnailHandle);

            if (err == EVROverlayError.None)
            {
                Log.Information("SteamVR Dashboard Overlay created successfully");
                OpenVR.Overlay.SetOverlayWidthInMeters(_dashboardOverlayHandle, 2.2f);
                OpenVR.Overlay.SetOverlayInputMethod(_dashboardOverlayHandle, VROverlayInputMethod.Mouse);

                if (_dashboardThumbnailHandle != OpenVR.k_ulOverlayHandleInvalid)
                {
                    using var thumb = VROverlayRenderer.RenderThumbnail();
                    OpenVR.Overlay.SetOverlayRaw(
                        _dashboardThumbnailHandle,
                        thumb.GetPixels(),
                        (uint)thumb.Width,
                        (uint)thumb.Height,
                        4);
                }
            }
            else
                Log.Warning("Failed to create Dashboard Overlay: {Error}", err);
        }
        else if (!config.VrDashboardOverlayEnabled && _dashboardOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            OpenVR.Overlay.DestroyOverlay(_dashboardOverlayHandle);
            _dashboardOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
            _dashboardThumbnailHandle = OpenVR.k_ulOverlayHandleInvalid;
        }

        // 2. In-VR notification strip — a separate, much smaller texture than the dashboard panel.
        if (config.VrFloatingOverlayEnabled)
        {
            if (_floatingOverlayHandle == OpenVR.k_ulOverlayHandleInvalid)
            {
                var err = OpenVR.Overlay.CreateOverlay(FloatingKey, "VRC Video Cacher HUD", ref _floatingOverlayHandle);
                if (err == EVROverlayError.None)
                {
                    Log.Information("VR Floating HUD Overlay created successfully");
                    OpenVR.Overlay.SetOverlayAlpha(_floatingOverlayHandle, 0.95f);
                }
                else
                    Log.Warning("Failed to create Floating Overlay: {Error}", err);
            }

            if (_floatingOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
            {
                OpenVR.Overlay.SetOverlayWidthInMeters(_floatingOverlayHandle, config.VrFloatingOverlayScale);
                var mat = CreateTransformMatrix(0f, NotificationVerticalOffset,
                    -config.VrFloatingOverlayDistance, NotificationPitch);
                OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(
                    _floatingOverlayHandle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref mat);

                SetFloatingShown(!config.VrFloatingOverlayOnlyWhenDownloading);
                _floatingKey = null;
            }
        }
        else if (!config.VrFloatingOverlayEnabled && _floatingOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            OpenVR.Overlay.DestroyOverlay(_floatingOverlayHandle);
            _floatingOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
            _floatingShown = false;
            _floatingKey = null;
        }
    }

    private void DestroyOverlays()
    {
        if (Overlay == null)
            return;

        if (_dashboardOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            OpenVR.Overlay.DestroyOverlay(_dashboardOverlayHandle);
            _dashboardOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
            _dashboardThumbnailHandle = OpenVR.k_ulOverlayHandleInvalid;
        }

        if (_floatingOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            OpenVR.Overlay.DestroyOverlay(_floatingOverlayHandle);
            _floatingOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
            _floatingShown = false;
            _floatingKey = null;
        }
    }

    private void SetFloatingShown(bool shown)
    {
        if (_floatingOverlayHandle == OpenVR.k_ulOverlayHandleInvalid || shown == _floatingShown)
            return;

        if (shown)
            OpenVR.Overlay.ShowOverlay(_floatingOverlayHandle);
        else
            OpenVR.Overlay.HideOverlay(_floatingOverlayHandle);

        _floatingShown = shown;
        _floatingKey = null; // force a fresh frame next time it appears
    }

    private async Task UpdateLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
            try
            {
                await Task.Delay(UpdateIntervalMs, token);

                if (Overlay == null)
                    continue;

                var config = ConfigManager.Config;
                if (!config.VrOverlayEnabled)
                    continue;

                var current = VideoDownloader.GetCurrentDownload();
                var progress = VideoDownloader.GetCurrentDownloadProgress();
                var queueCount = VideoDownloader.GetQueueCount();
                var hasActivity = current != null || queueCount > 0;
                if (hasActivity)
                    _lastActivityUtc = DateTime.UtcNow;

                // Only ever toggle visibility on an actual transition — polling Show/Hide every tick is
                // what made the old overlay flicker.
                if (_floatingOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
                    SetFloatingShown(!config.VrFloatingOverlayOnlyWhenDownloading ||
                                     hasActivity ||
                                     DateTime.UtcNow - _lastActivityUtc < HideGrace);

                var seconds = (float)_clock.Elapsed.TotalSeconds;
                // Only render while there is real activity; during the hide-grace window we keep the last
                // frame instead of flashing an "Idle" card. In always-on mode the idle card is wanted.
                if (_floatingShown && (hasActivity || !config.VrFloatingOverlayOnlyWhenDownloading))
                    RenderFloating(current, queueCount, progress, seconds);

                if (_dashboardOverlayHandle != OpenVR.k_ulOverlayHandleInvalid &&
                    OpenVR.Overlay.IsOverlayVisible(_dashboardOverlayHandle))
                    RenderDashboard(seconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during VR Overlay update: {Message}", ex.Message);
            }
    }

    private void RenderFloating(VideoInfo? current, int queueCount, double? progress, float seconds)
    {
        // Re-render only when something visible changed: a sliding pulse ticks at ~5 fps while the total
        // size is unknown, otherwise the bar only moves when the real percentage does.
        var animBucket = current != null && progress is null ? (int)(seconds * 5) : 0;
        var key =
            $"{current?.VideoId}|{current?.UrlType}|{current?.DownloadFormat}|{queueCount}|{progress:F3}|{animBucket}";
        if (key == _floatingKey)
            return;

        _floatingKey = key;
        using var frame = VROverlayRenderer.RenderNotification(current, queueCount, progress, seconds);
        PushFrame(_floatingOverlayHandle, frame);
    }

    private void RenderDashboard(float seconds)
    {
        var now = Environment.TickCount64;
        if (now - _lastDashboardRenderTicks < DashboardRenderIntervalMs)
            return;
        _lastDashboardRenderTicks = now;

        var status = StatusService.Current;
        var statusText = status.IsBusy ? status.Text : "Idle";
        using var frame = VROverlayRenderer.RenderDashboard(
            VideoDownloader.GetCurrentDownload(),
            VideoDownloader.GetQueueSnapshot(),
            statusText,
            CacheManager.GetTotalCacheSize(),
            ConfigManager.Config.CacheMaxSizeInGb,
            seconds);
        PushFrame(_dashboardOverlayHandle, frame);
    }

    private static void PushFrame(ulong handle, SKBitmap frame)
    {
        if (handle == OpenVR.k_ulOverlayHandleInvalid)
            return;
        OpenVR.Overlay.SetOverlayRaw(handle, frame.GetPixels(), (uint)frame.Width, (uint)frame.Height, 4);
    }

    private static HmdMatrix34_t CreateTransformMatrix(float x, float y, float z, float pitchDegrees = 0f)
    {
        var rad = pitchDegrees * (MathF.PI / 180f);
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);

        return new()
        {
            m0 = 1.0f,
            m1 = 0.0f,
            m2 = 0.0f,
            m3 = x,
            m4 = 0.0f,
            m5 = cos,
            m6 = -sin,
            m7 = y,
            m8 = 0.0f,
            m9 = sin,
            m10 = cos,
            m11 = z
        };
    }
}