using SkiaSharp;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

public static class VROverlayRenderer
{
    public const int OverlayWidth = 1024;
    public const int OverlayHeight = 576;
    public const int ThumbnailSize = 256;

    // The in-VR notification strip is a separate, much smaller texture (4:1) so it can sit low in the
    // view like a subtitle instead of covering the game.
    public const int NotificationWidth = 640;
    public const int NotificationHeight = 160;

    private static readonly SKTypeface DefaultTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal);
    private static readonly SKTypeface BoldTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);

    /// <summary>
    /// Render the full SteamVR dashboard status panel to an SKBitmap (1024x576, Rgba8888).
    /// </summary>
    public static SKBitmap RenderDashboard(
        VideoInfo? currentDownload,
        IReadOnlyList<VideoInfo> queuedDownloads,
        string statusText,
        long cacheSizeBytes,
        float maxCacheGb,
        float progressAnim)
    {
        var bitmap = new SKBitmap(OverlayWidth, OverlayHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        // 1. Background
        canvas.Clear(new(16, 18, 27, 240));

        // Outer glow/border
        using var borderPaint = new SKPaint();
        borderPaint.Color = new(56, 189, 248, 120);
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.StrokeWidth = 3;
        borderPaint.IsAntialias = true;
        canvas.DrawRoundRect(4, 4, OverlayWidth - 8, OverlayHeight - 8, 20, 20, borderPaint);

        // Header Background
        using var headerBgPaint = new SKPaint();
        headerBgPaint.Color = new(24, 28, 42, 255);
        headerBgPaint.Style = SKPaintStyle.Fill;
        headerBgPaint.IsAntialias = true;
        canvas.DrawRoundRect(16, 16, OverlayWidth - 32, 70, 14, 14, headerBgPaint);

        // App Title
        using var titlePaint = new SKPaint();
        titlePaint.Color = new(255, 255, 255);
        titlePaint.IsAntialias = true;
        using var titleFont = new SKFont(BoldTypeface, 30);
        canvas.DrawText("VRC Video Cacher", 36, 62, titleFont, titlePaint);

        // Status Badge (Top Right)
        var isDownloading = currentDownload != null;
        var badgeBgColor = isDownloading ? new SKColor(16, 185, 129, 220) : new SKColor(75, 85, 99, 180);
        var badgeText = isDownloading ? "DOWNLOADING" : "IDLE";

        using var badgeBgPaint = new SKPaint();
        badgeBgPaint.Color = badgeBgColor;
        badgeBgPaint.Style = SKPaintStyle.Fill;
        badgeBgPaint.IsAntialias = true;
        const float badgeWidth = 200;
        const float badgeHeight = 42;
        const float badgeX = OverlayWidth - 36 - badgeWidth;
        const float badgeY = 30;
        canvas.DrawRoundRect(badgeX, badgeY, badgeWidth, badgeHeight, 10, 10, badgeBgPaint);

        using var badgeTextFont = new SKFont(BoldTypeface, 18);
        using var badgeTextPaint = new SKPaint();
        badgeTextPaint.Color = SKColors.White;
        badgeTextPaint.IsAntialias = true;
        canvas.DrawText(badgeText, badgeX + (badgeWidth - 140) / 2, badgeY + 28, badgeTextFont, badgeTextPaint);

        // 2. Current Download Card
        const float cardY = 102;
        const float cardHeight = 220;
        using var cardBgPaint = new SKPaint();
        cardBgPaint.Color = new(28, 33, 50, 230);
        cardBgPaint.Style = SKPaintStyle.Fill;
        cardBgPaint.IsAntialias = true;
        canvas.DrawRoundRect(16, cardY, OverlayWidth - 32, cardHeight, 16, 16, cardBgPaint);

        // Card Border
        using var cardBorderPaint = new SKPaint();
        cardBorderPaint.Color = isDownloading ? new(56, 189, 248, 80) : new SKColor(255, 255, 255, 20);
        cardBorderPaint.Style = SKPaintStyle.Stroke;
        cardBorderPaint.StrokeWidth = 2;
        cardBorderPaint.IsAntialias = true;
        canvas.DrawRoundRect(16, cardY, OverlayWidth - 32, cardHeight, 16, 16, cardBorderPaint);

        // Section Label
        using var labelFont = new SKFont(BoldTypeface, 18);
        using var labelPaint = new SKPaint();
        labelPaint.Color = new(56, 189, 248);
        labelPaint.IsAntialias = true;
        canvas.DrawText("CURRENT ACTIVITY", 40, cardY + 38, labelFont, labelPaint);

        if (currentDownload != null)
        {
            // Video Title / ID
            using var videoTitleFont = new SKFont(BoldTypeface, 26);
            using var videoTitlePaint = new SKPaint();
            videoTitlePaint.Color = SKColors.White;
            videoTitlePaint.IsAntialias = true;

            var titleStr = TruncateText($"Video ID: {currentDownload.VideoId}", 45);
            canvas.DrawText(titleStr, 40, cardY + 80, videoTitleFont, videoTitlePaint);

            // Tags (Source & Format)
            DrawTag(canvas, currentDownload.UrlType.ToString(), 40, cardY + 104, new(99, 102, 241));
            DrawTag(canvas, currentDownload.DownloadFormat.ToString(), 170, cardY + 104, new(236, 72, 153));

            // URL
            using var urlFont = new SKFont(DefaultTypeface, 16);
            using var urlPaint = new SKPaint();
            urlPaint.Color = new(156, 163, 175);
            urlPaint.IsAntialias = true;
            var urlStr = TruncateText(currentDownload.VideoUrl, 65);
            canvas.DrawText(urlStr, 40, cardY + 155, urlFont, urlPaint);

            // Animated Progress Bar
            const float barX = 40;
            const float barY = cardY + 175;
            const float barW = OverlayWidth - 80;
            const float barH = 16;

            using var barBgPaint = new SKPaint();
            barBgPaint.Color = new(45, 55, 72);
            barBgPaint.Style = SKPaintStyle.Fill;
            barBgPaint.IsAntialias = true;
            canvas.DrawRoundRect(barX, barY, barW, barH, 8, 8, barBgPaint);

            // Moving stripe / indeterminate bar
            const float fillWidth = barW * 0.35f;
            var fillStart = barX + (barW - fillWidth) * ((MathF.Sin(progressAnim) + 1f) / 2f);

            using var barFillPaint = new SKPaint();
            barFillPaint.Color = new(56, 189, 248);
            barFillPaint.Style = SKPaintStyle.Fill;
            barFillPaint.IsAntialias = true;
            canvas.DrawRoundRect(fillStart, barY, fillWidth, barH, 8, 8, barFillPaint);
        }
        else
        {
            using var idleFont = new SKFont(DefaultTypeface, 24);
            using var idlePaint = new SKPaint();
            idlePaint.Color = new(156, 163, 175);
            idlePaint.IsAntialias = true;
            canvas.DrawText("No active download in progress", 40, cardY + 95, idleFont, idlePaint);

            using var idleSubFont = new SKFont(DefaultTypeface, 18);
            using var idleSubPaint = new SKPaint();
            idleSubPaint.Color = new(107, 114, 128);
            idleSubPaint.IsAntialias = true;
            canvas.DrawText("Video requests from VRChat video players will appear here automatically.", 40, cardY + 135,
                idleSubFont, idleSubPaint);
        }

        // 3. Queue Section
        const float queueY = 338;
        const float queueHeight = 160;
        using var queueBgPaint = new SKPaint();
        queueBgPaint.Color = new(22, 27, 40, 220);
        queueBgPaint.Style = SKPaintStyle.Fill;
        queueBgPaint.IsAntialias = true;
        canvas.DrawRoundRect(16, queueY, OverlayWidth - 32, queueHeight, 16, 16, queueBgPaint);

        using var queueTitleFont = new SKFont(BoldTypeface, 18);
        using var queueTitlePaint = new SKPaint();
        queueTitlePaint.Color = new(167, 139, 250);
        queueTitlePaint.IsAntialias = true;
        canvas.DrawText($"DOWNLOAD QUEUE ({queuedDownloads.Count})", 40, queueY + 34, queueTitleFont, queueTitlePaint);

        if (queuedDownloads.Count > 0)
        {
            using var itemFont = new SKFont(DefaultTypeface, 18);
            using var itemPaint = new SKPaint();
            itemPaint.Color = new(229, 231, 235);
            itemPaint.IsAntialias = true;

            var displayCount = Math.Min(queuedDownloads.Count, 3);
            for (var i = 0; i < displayCount; i++)
            {
                var item = queuedDownloads[i];
                var line = $"{i + 1}. {item.VideoId}  [{item.UrlType} / {item.DownloadFormat}]";
                canvas.DrawText(TruncateText(line, 60), 40, queueY + 70 + i * 30, itemFont, itemPaint);
            }

            if (queuedDownloads.Count > 3)
            {
                using var moreFont = new SKFont(DefaultTypeface, 15);
                using var morePaint = new SKPaint();
                morePaint.Color = new(156, 163, 175);
                morePaint.IsAntialias = true;
                canvas.DrawText($"+ {queuedDownloads.Count - 3} more items in queue", 40, queueY + 150, moreFont,
                    morePaint);
            }
        }
        else
        {
            using var emptyFont = new SKFont(DefaultTypeface, 18);
            using var emptyPaint = new SKPaint();
            emptyPaint.Color = new(107, 114, 128);
            emptyPaint.IsAntialias = true;
            canvas.DrawText("Queue is empty", 40, queueY + 80, emptyFont, emptyPaint);
        }

        // 4. Footer Bar (Bottom stats)
        const float footerY = 510;
        using var footerBgPaint = new SKPaint();
        footerBgPaint.Color = new(20, 24, 36, 255);
        footerBgPaint.Style = SKPaintStyle.Fill;
        footerBgPaint.IsAntialias = true;
        canvas.DrawRoundRect(16, footerY, OverlayWidth - 32, 50, 10, 10, footerBgPaint);

        var usedGb = cacheSizeBytes / (1024f * 1024f * 1024f);
        var cacheStr = maxCacheGb > 0
            ? $"Cache: {usedGb:F2} GB / {maxCacheGb:F1} GB"
            : $"Cache: {usedGb:F2} GB";

        using var footerFont = new SKFont(DefaultTypeface, 16);
        using var footerPaint = new SKPaint();
        footerPaint.Color = new(156, 163, 175);
        footerPaint.IsAntialias = true;
        canvas.DrawText(cacheStr, 36, footerY + 32, footerFont, footerPaint);

        var rightStatusStr = !string.IsNullOrWhiteSpace(statusText) ? statusText : "Ready";
        canvas.DrawText(TruncateText(rightStatusStr, 35), OverlayWidth - 360, footerY + 32, footerFont, footerPaint);

        return bitmap;
    }

    /// <summary>
    /// Render the compact in-VR notification strip (640x160, Rgba8888): a subtitle-like bar showing the
    /// current video, a real progress bar (or a sliding pulse while the total is unknown) and queue count.
    /// </summary>
    public static SKBitmap RenderNotification(
        VideoInfo? currentDownload,
        int queuedCount,
        double? progress,
        float timeSeconds)
    {
        var bitmap = new SKBitmap(NotificationWidth, NotificationHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Card + border
        using var cardPaint = new SKPaint();
        cardPaint.Color = new(16, 18, 27, 235);
        cardPaint.Style = SKPaintStyle.Fill;
        cardPaint.IsAntialias = true;
        canvas.DrawRoundRect(8, 8, NotificationWidth - 16, NotificationHeight - 16, 18, 18, cardPaint);

        using var borderPaint = new SKPaint();
        borderPaint.Color = new(56, 189, 248, 110);
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.StrokeWidth = 2;
        borderPaint.IsAntialias = true;
        canvas.DrawRoundRect(8, 8, NotificationWidth - 16, NotificationHeight - 16, 18, 18, borderPaint);

        // Header: accent dot + app title (+ queue badge on the right)
        using var dotPaint = new SKPaint();
        dotPaint.Color = new(56, 189, 248);
        dotPaint.Style = SKPaintStyle.Fill;
        dotPaint.IsAntialias = true;
        canvas.DrawCircle(34, 42, 6, dotPaint);

        using var titleFont = new SKFont(BoldTypeface, 16);
        using var titlePaint = new SKPaint();
        titlePaint.Color = new(148, 163, 184);
        titlePaint.IsAntialias = true;
        canvas.DrawText("VRC Video Cacher", 50, 48, titleFont, titlePaint);

        if (queuedCount > 0)
        {
            using var queueFont = new SKFont(BoldTypeface, 15);
            using var queuePaint = new SKPaint();
            queuePaint.Color = new(167, 139, 250);
            queuePaint.IsAntialias = true;
            DrawRight(canvas, $"+{queuedCount} queued", NotificationWidth - 30, 48, queueFont, queuePaint);
        }

        var isDownloading = currentDownload != null;

        // Body: video id (left) and source/format (right)
        using var idFont = new SKFont(BoldTypeface, 24);
        using var idPaint = new SKPaint();
        idPaint.Color = SKColors.White;
        idPaint.IsAntialias = true;

        if (currentDownload != null)
        {
            canvas.DrawText(TruncateText(currentDownload.VideoId, 34), 30, 94, idFont, idPaint);

            using var metaFont = new SKFont(DefaultTypeface, 15);
            using var metaPaint = new SKPaint();
            metaPaint.Color = new(148, 163, 184);
            metaPaint.IsAntialias = true;
            DrawRight(canvas, $"{currentDownload.UrlType} · {currentDownload.DownloadFormat}",
                NotificationWidth - 30, 92, metaFont, metaPaint);
        }
        else
        {
            using var idlePaint = new SKPaint();
            idlePaint.Color = new(148, 163, 184);
            idlePaint.IsAntialias = true;
            canvas.DrawText("Idle", 30, 94, idFont, idlePaint);
        }

        // Progress bar (leaves room on the right for the percentage)
        const float barX = 30;
        const float barY = 112;
        const float barH = 14;
        const float barW = NotificationWidth - 30 - 90;

        using var barBgPaint = new SKPaint();
        barBgPaint.Color = new(45, 55, 72);
        barBgPaint.IsAntialias = true;
        canvas.DrawRoundRect(barX, barY, barW, barH, 7, 7, barBgPaint);

        using var barFillPaint = new SKPaint();
        barFillPaint.Color = new(56, 189, 248);
        barFillPaint.IsAntialias = true;

        if (progress is { } fraction)
        {
            var fillW = barW * (float)Math.Clamp(fraction, 0, 1);
            if (fillW > 1)
                canvas.DrawRoundRect(barX, barY, Math.Max(fillW, barH), barH, 7, 7, barFillPaint);
        }
        else if (isDownloading)
        {
            // Unknown total: slide a fixed-width pulse back and forth.
            const float pulse = 0.28f;
            var pulseW = barW * pulse;
            var pulseX = barX + (barW - pulseW) * ((MathF.Sin(timeSeconds * 3f) + 1f) / 2f);
            canvas.DrawRoundRect(pulseX, barY, pulseW, barH, 7, 7, barFillPaint);
        }

        if (isDownloading)
        {
            using var pctFont = new SKFont(BoldTypeface, 16);
            using var pctPaint = new SKPaint();
            pctPaint.Color = progress is null ? new SKColor(148, 163, 184) : new SKColor(56, 189, 248);
            pctPaint.IsAntialias = true;
            DrawRight(canvas, progress is { } p ? $"{p * 100:0}%" : "...",
                NotificationWidth - 30, 124, pctFont, pctPaint);
        }

        return bitmap;
    }

    /// <summary>
    /// Render the 256x256 thumbnail icon for SteamVR Dashboard.
    /// </summary>
    public static SKBitmap RenderThumbnail()
    {
        var bitmap = new SKBitmap(ThumbnailSize, ThumbnailSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(new(16, 18, 27, 255));

        // Rounded glowing background box
        using var bgPaint = new SKPaint();
        bgPaint.Color = new(30, 41, 59, 255);
        bgPaint.Style = SKPaintStyle.Fill;
        bgPaint.IsAntialias = true;
        canvas.DrawRoundRect(12, 12, ThumbnailSize - 24, ThumbnailSize - 24, 28, 28, bgPaint);

        using var borderPaint = new SKPaint();
        borderPaint.Color = new(56, 189, 248, 200);
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.StrokeWidth = 4;
        borderPaint.IsAntialias = true;
        canvas.DrawRoundRect(12, 12, ThumbnailSize - 24, ThumbnailSize - 24, 28, 28, borderPaint);

        // Play / Download shape
        using var playPaint = new SKPaint();
        playPaint.Color = new(56, 189, 248);
        playPaint.Style = SKPaintStyle.Fill;
        playPaint.IsAntialias = true;
        using var path = new SKPath();
        path.MoveTo(90, 60);
        path.LineTo(175, 110);
        path.LineTo(90, 160);
        path.Close();
        canvas.DrawPath(path, playPaint);

        // Text "VVC"
        using var font = new SKFont(BoldTypeface, 34);
        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.White;
        textPaint.IsAntialias = true;
        canvas.DrawText("VVC", 86, 215, font, textPaint);

        return bitmap;
    }

    private static void DrawTag(SKCanvas canvas, string text, float x, float y, SKColor color)    {
        using var font = new SKFont(BoldTypeface, 14);
        float width = Math.Max(60, text.Length * 11 + 20);
        const float height = 28;

        using var bgPaint = new SKPaint();
        bgPaint.Color = color;
        bgPaint.Style = SKPaintStyle.Fill;
        bgPaint.IsAntialias = true;
        canvas.DrawRoundRect(x, y, width, height, 6, 6, bgPaint);

        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.White;
        textPaint.IsAntialias = true;
        canvas.DrawText(text, x + 10, y + 20, font, textPaint);
    }

    private static string TruncateText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;
        return text[..(maxChars - 3)] + "...";
    }

    /// <summary>Draw <paramref name="text"/> so that it ends at <paramref name="right"/>.</summary>
    private static void DrawRight(SKCanvas canvas, string text, float right, float baseline, SKFont font,
        SKPaint paint) =>
        canvas.DrawText(text, right - font.MeasureText(text, paint), baseline, font, paint);
}