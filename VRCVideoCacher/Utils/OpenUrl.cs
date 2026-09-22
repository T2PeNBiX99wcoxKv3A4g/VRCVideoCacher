using System.Diagnostics;
using JetBrains.Annotations;
using VRCVideoCacher.Extensions;

namespace VRCVideoCacher.Utils;

public partial class OpenUrl : Singleton<OpenUrl>
{
    [PublicAPI]
    public bool Open2(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Warning("Refused to open empty URL");
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Log.Warning("Refused to open invalid URL {Url}", url);
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Warning("Refused to open non-web URL {Url}", url);
            return false;
        }

        return Try.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            };
            return Process.Start(psi) != null;
        }).GetOrElse(ex =>
        {
            Log.Error(ex, "Failed to open link: {Url}", url);
            return false;
        });
    }
}