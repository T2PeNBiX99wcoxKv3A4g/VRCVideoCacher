using JetBrains.Annotations;
using Valve.VR;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services;

public partial class VROverlayService : Singleton<VROverlayService>
{
    private const string DashboardKey = "com.github.ellyvr.vrcvideocacher.dashboard";
    private const string FloatingKey = "com.github.ellyvr.vrcvideocacher.floating";

    private ulong _dashboardOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
    private ulong _dashboardThumbnailHandle = OpenVR.k_ulOverlayHandleInvalid;
    private ulong _floatingOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;

    private CancellationTokenSource? _updateLoopCts;
    private float _progressAnim;
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
        _updateLoopCts?.Dispose();
        _updateLoopCts = new();

        _ = Task.Run(() => UpdateLoop(_updateLoopCts.Token));

        ConfigManager.OnConfigChanged += OnConfigChanged;
    }

    [PublicAPI]
    public void Stop2()
    {
        ConfigManager.OnConfigChanged -= OnConfigChanged;

        _updateLoopCts?.Cancel();
        _updateLoopCts?.Dispose();
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

        // 1. Dashboard Overlay
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

                // Set thumbnail icon for Dashboard
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

        // 2. In-VR Floating Overlay (HUD)
        if (config.VrFloatingOverlayEnabled)
        {
            if (_floatingOverlayHandle == OpenVR.k_ulOverlayHandleInvalid)
            {
                var err = OpenVR.Overlay.CreateOverlay(FloatingKey, "VRC Video Cacher HUD", ref _floatingOverlayHandle);
                if (err == EVROverlayError.None)
                {
                    Log.Information("VR Floating HUD Overlay created successfully");
                    OpenVR.Overlay.SetOverlayWidthInMeters(_floatingOverlayHandle, config.VrFloatingOverlayScale);
                    OpenVR.Overlay.SetOverlayAlpha(_floatingOverlayHandle, 0.95f);

                    // Position relative to HMD: lower and tilted up towards eyes
                    var mat = CreateTransformMatrix(0.0f, -0.28f, -config.VrFloatingOverlayDistance, 12f);
                    OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(
                        _floatingOverlayHandle,
                        OpenVR.k_unTrackedDeviceIndex_Hmd,
                        ref mat);

                    if (config.VrFloatingOverlayOnlyWhenDownloading)
                        OpenVR.Overlay.HideOverlay(_floatingOverlayHandle);
                    else
                        OpenVR.Overlay.ShowOverlay(_floatingOverlayHandle);
                }
                else
                    Log.Warning("Failed to create Floating Overlay: {Error}", err);
            }
            else
            {
                OpenVR.Overlay.SetOverlayWidthInMeters(_floatingOverlayHandle, config.VrFloatingOverlayScale);
                var mat = CreateTransformMatrix(0.0f, -0.28f, -config.VrFloatingOverlayDistance, 12f);
                OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(
                    _floatingOverlayHandle,
                    OpenVR.k_unTrackedDeviceIndex_Hmd,
                    ref mat);
            }
        }
        else if (!config.VrFloatingOverlayEnabled && _floatingOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            OpenVR.Overlay.DestroyOverlay(_floatingOverlayHandle);
            _floatingOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
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
        }
    }

    private async Task UpdateLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
            try
            {
                await Task.Delay(300, token);

                if (Overlay == null)
                    continue;

                var config = ConfigManager.Config;
                if (!config.VrOverlayEnabled)
                    continue;

                var currentDownload = VideoDownloader.GetCurrentDownload();
                var queueSnapshot = VideoDownloader.GetQueueSnapshot();
                var hasActivity = currentDownload != null || queueSnapshot.Count > 0;

                // Check floating overlay visibility condition
                if (_floatingOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
                {
                    if (config.VrFloatingOverlayOnlyWhenDownloading)
                    {
                        if (hasActivity && !OpenVR.Overlay.IsOverlayVisible(_floatingOverlayHandle))
                            OpenVR.Overlay.ShowOverlay(_floatingOverlayHandle);
                        else if (!hasActivity && OpenVR.Overlay.IsOverlayVisible(_floatingOverlayHandle))
                            OpenVR.Overlay.HideOverlay(_floatingOverlayHandle);
                    }
                    else if (!OpenVR.Overlay.IsOverlayVisible(_floatingOverlayHandle))
                        OpenVR.Overlay.ShowOverlay(_floatingOverlayHandle);
                }

                var isDashboardVisible = _dashboardOverlayHandle != OpenVR.k_ulOverlayHandleInvalid &&
                                         OpenVR.Overlay.IsOverlayVisible(_dashboardOverlayHandle);
                var isFloatingVisible = _floatingOverlayHandle != OpenVR.k_ulOverlayHandleInvalid &&
                                        OpenVR.Overlay.IsOverlayVisible(_floatingOverlayHandle);

                // Only render if at least one overlay is currently visible
                if (!isDashboardVisible && !isFloatingVisible)
                    continue;

                _progressAnim += 0.25f;
                if (_progressAnim > MathF.PI * 2)
                    _progressAnim -= MathF.PI * 2;

                var statusSnapshot = StatusService.Current;
                var statusText = statusSnapshot.IsBusy ? statusSnapshot.Text : "Idle";
                var cacheSize = CacheManager.GetTotalCacheSize();
                var maxCacheGb = config.CacheMaxSizeInGb;

                using var frame = VROverlayRenderer.RenderPanel(
                    currentDownload,
                    queueSnapshot,
                    statusText,
                    cacheSize,
                    maxCacheGb,
                    _progressAnim);

                var pixels = frame.GetPixels();
                var width = (uint)frame.Width;
                var height = (uint)frame.Height;

                if (isDashboardVisible)
                    OpenVR.Overlay.SetOverlayRaw(_dashboardOverlayHandle, pixels, width, height, 4);

                if (isFloatingVisible)
                    OpenVR.Overlay.SetOverlayRaw(_floatingOverlayHandle, pixels, width, height, 4);
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