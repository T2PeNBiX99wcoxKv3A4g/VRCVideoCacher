using System.Text.Json;
using VRCVideoCacher.Services;
using VRCVideoCacher.Services.Nico;

namespace VRCVideoCacher.Tests;

public class NicoLiveTests
{
    private const string LiveJson = """
    {
      "akashic" : {
        "isRunning" : false,
        "enabled" : true
      },
      "site" : {
        "locale" : "ja_JP",
        "serverTime" : 1790370065672,
        "frontendVersion" : "655.0.0",
        "apiBaseUrl" : "https://live.nicovideo.jp/",
        "frontendId" : 9,
        "relive" : {
          "apiBaseUrl" : "https://live2.nicovideo.jp/",
          "channelApiBaseUrl" : "https://channel.live2.nicovideo.jp/",
          "webSocketUrl" : "wss://a.live2.nicovideo.jp/unama/wsapi/v2/watch/53243860222558?audience_token=53243860222558_anonymous-user-ca1f7127-2c62-4d4f-9975-b8137cd0d367_1790456465_d6a2262bb00807603ca91e92f0093b636bc600f8",
          "csrfToken" : "60IoYr1PPjEteRBg",
          "audienceToken" : "anonymous-user-ca1f7127-2c62-4d4f-9975-b8137cd0d367_1790456465_e13fc04ad0f1ea7279d1800c92d11a7407a5e25a"
        }
      },
      "program" : {
        "nicoliveProgramId" : "lv351467594",
        "providerType" : "community",
        "visualProviderType" : "community",
        "title" : "【N_Airボイチェン配信】のんびりゲーム配信_絶望の朝バンカラマッチ",
        "thumbnail" : {
          "small" : "https://secure-dcdn.cdn.nimg.jp/comch/community-icon/64x64/404.jpg",
          "huge" : {
            "s1920x1080" : "https://listing-thumbnail.live.nicovideo.jp?image=prod-lv351467594/thumbnail_1790023502745.png&w=1920&h=1080&v=1790364125593",
            "s1280x720" : "https://listing-thumbnail.live.nicovideo.jp?image=prod-lv351467594/thumbnail_1790023502745.png&w=1280&h=720&v=1790364125725",
            "s640x360" : "https://listing-thumbnail.live.nicovideo.jp?image=prod-lv351467594/thumbnail_1790023502745.png&w=640&h=360&v=1790364125857",
            "s352x198" : "https://listing-thumbnail.live.nicovideo.jp?image=prod-lv351467594/thumbnail_1790023502745.png&w=352&h=198&v=1790364125994"
          }
        },
        "supplier" : {
          "supplierType" : "user",
          "name" : "アカザクラ",
          "pageUrl" : "http://www.nicovideo.jp/user/77208790"
        },
        "openTime" : 1790364376,
        "beginTime" : 1790364376,
        "endTime" : 1790375176,
        "status" : "ON_AIR",
        "description" : "初放送です（535回目）",
        "tag" : {
          "list" : [ {
            "text" : "ゲーム",
            "type" : "category",
            "isLocked" : true
          }, {
            "text" : "コメント歓迎",
            "type" : "",
            "isLocked" : true
          } ]
        },
        "statistics" : {
          "watchCount" : 22,
          "commentCount" : 109,
          "timeshiftReservationCount" : null
        }
      }
    }
    """;

    private const string NormalVideoJson = """
    {
      "meta" : {
        "status" : 200,
        "code" : "HTTP_200"
      },
      "data" : {
        "response" : {
          "client" : {
            "nicosid" : "1790370143.237346801",
            "watchId" : "so46814193",
            "watchTrackId" : "HP7UlmGHWT_1790370143670"
          },
          "media" : {
            "domand" : {
              "videos" : [ {
                "id" : "video-h264-1080p",
                "isAvailable" : false,
                "label" : "1080p"
              }, {
                "id" : "video-h264-720p",
                "isAvailable" : true,
                "label" : "720p"
              } ],
              "audios" : [ {
                "id" : "audio-aac-192kbps",
                "isAvailable" : true
              } ],
              "accessRightKey" : "sample_access_right_key"
            }
          },
          "video" : {
            "id" : "so46814193",
            "title" : "元祖！バンドリちゃん 第51話「明けの明星」",
            "description" : "暗黒客船デゼスポワール号",
            "duration" : 90,
            "count" : {
              "view" : 18683,
              "comment" : 934,
              "mylist" : 36,
              "like" : 497
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void Test_Deserialize_NicoLivePageData()
    {
        var data = JsonSerializer.Deserialize(LiveJson, NicoJsonContext.Default.NicoLivePageData);
        Assert.NotNull(data);
        Assert.NotNull(data.Site);
        Assert.Equal(9, data.Site.FrontendId);
        Assert.NotNull(data.Site.Relive);
        Assert.StartsWith("wss://a.live2.nicovideo.jp/unama/wsapi/v2/watch/", data.Site.Relive.WebSocketUrl);
        Assert.Equal("60IoYr1PPjEteRBg", data.Site.Relive.CsrfToken);

        Assert.NotNull(data.Program);
        Assert.Equal("lv351467594", data.Program.NicoliveProgramId);
        Assert.Equal("【N_Airボイチェン配信】のんびりゲーム配信_絶望の朝バンカラマッチ", data.Program.Title);
        Assert.Equal("ON_AIR", data.Program.Status);
        Assert.NotNull(data.Program.Supplier);
        Assert.Equal("アカザクラ", data.Program.Supplier.Name);

        Assert.NotNull(data.Program.Thumbnail);
        Assert.NotNull(data.Program.Thumbnail.Huge);
        Assert.Contains("thumbnail_1790023502745.png", data.Program.Thumbnail.Huge.S1280X720);

        Assert.NotNull(data.Program.Tag);
        Assert.NotNull(data.Program.Tag.List);
        Assert.Equal(2, data.Program.Tag.List.Count);
        Assert.Equal("ゲーム", data.Program.Tag.List[0].Text);

        Assert.NotNull(data.Program.Statistics);
        Assert.Equal(22, data.Program.Statistics.WatchCount);
        Assert.Equal(109, data.Program.Statistics.CommentCount);
    }

    [Fact]
    public void Test_Deserialize_NicoNormalVideoPageData()
    {
        var data = JsonSerializer.Deserialize(NormalVideoJson, NicoJsonContext.Default.NicoWatchPageData);
        Assert.NotNull(data);
        Assert.NotNull(data.Data?.Response);
        var res = data.Data.Response;
        Assert.Equal("元祖！バンドリちゃん 第51話「明けの明星」", res.Video?.Title);
        Assert.Equal(90, res.Video?.Duration);
        Assert.Equal(18683, res.Video?.Count?.View);
        Assert.Equal("HP7UlmGHWT_1790370143670", res.Client?.WatchTrackId);
        Assert.Equal("sample_access_right_key", res.Media?.Domand?.AccessRightKey);
    }

    [Fact]
    public void Test_WebSocket_Messages_Serialization()
    {
        var startWatching = new NicoWsStartWatchingMessage
        {
            Type = "startWatching",
            Data = new()
            {
                Reconnect = false,
                Room = new()
                {
                    Protocol = "webSocket",
                    Commentable = true
                },
                Stream = new()
                {
                    AccessRightMethod = "single_cookie",
                    ChasePlay = false,
                    Latency = "high",
                    Protocol = "hls",
                    Quality = "abr"
                }
            }
        };

        var json = JsonSerializer.Serialize(startWatching, NicoJsonContext.Default.NicoWsStartWatchingMessage);
        Assert.Contains("\"type\":\"startWatching\"", json);
        Assert.Contains("\"accessRightMethod\":\"single_cookie\"", json);
        Assert.Contains("\"protocol\":\"hls\"", json);
        Assert.Contains("\"quality\":\"abr\"", json);

        const string streamJson = """
        {
          "type": "stream",
          "data": {
            "uri": "https://example.com/master.m3u8?token=xyz",
            "quality": "abr",
            "protocol": "hls",
            "cookies": [
              {
                "name": "nicosid",
                "value": "12345"
              },
              {
                "name": "user_session",
                "value": "abcde"
              }
            ]
          }
        }
        """;

        var streamMsg = JsonSerializer.Deserialize(streamJson, NicoJsonContext.Default.NicoWsStreamMessage);
        Assert.NotNull(streamMsg);
        Assert.Equal("stream", streamMsg.Type);
        Assert.NotNull(streamMsg.Data);
        Assert.Equal("https://example.com/master.m3u8?token=xyz", streamMsg.Data.Uri);
        Assert.NotNull(streamMsg.Data.Cookies);
        Assert.Equal(2, streamMsg.Data.Cookies.Count);
        Assert.Equal("nicosid", streamMsg.Data.Cookies[0].Name);
        Assert.Equal("12345", streamMsg.Data.Cookies[0].Value);

        var pongMsg = new NicoWsSimpleMessage { Type = "pong" };
        var pongJson = JsonSerializer.Serialize(pongMsg, NicoJsonContext.Default.NicoWsSimpleMessage);
        Assert.Contains("\"type\":\"pong\"", pongJson);
    }

    [Theory]
    [InlineData("sm46814193", true, false)]
    [InlineData("so46814193", true, false)]
    [InlineData("nm12345", true, false)]
    [InlineData("lv351467594", true, true)]
    [InlineData("LV351467594", true, true)]
    [InlineData("youtube123", false, false)]
    public void Test_IdValidation(string id, bool isVideo, bool isLive)
    {
        Assert.Equal(isVideo, NicoVideoApiService.IsValidVideoId(id));
        Assert.Equal(isLive, NicoVideoApiService.IsValidLiveId(id));
    }

    [Fact]
    public void Test_ParseMasterPlaylist()
    {
        const string masterM3u8 = """
        #EXTM3U
        #EXT-X-VERSION:6
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio-aac-192k",NAME="audio",DEFAULT=YES,AUTOSELECT=YES,URI="audio/playlist.m3u8"
        #EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720,AUDIO="audio-aac-192k"
        720p/playlist.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=4000000,RESOLUTION=1920x1080,AUDIO="audio-aac-192k"
        1080p/playlist.m3u8
        """;

        var (videoUrl, audioUrl) = NicoHlsSession.ParseMasterPlaylist(masterM3u8, "https://example.com/live/master.m3u8");
        Assert.Equal("https://example.com/live/1080p/playlist.m3u8", videoUrl);
        Assert.Equal("https://example.com/live/audio/playlist.m3u8", audioUrl);
    }

    [Fact]
    public void Test_ParseVariantPlaylist()
    {
        const string variantM3u8 = """
        #EXTM3U
        #EXT-X-VERSION:6
        #EXT-X-TARGETDURATION:2
        #EXT-X-MEDIA-SEQUENCE:1050
        #EXT-X-MAP:URI="init.mp4"
        #EXT-X-KEY:METHOD=AES-128,URI="https://example.com/key",IV=0x0000000000000000000000000000041a
        #EXTINF:2.000,
        seg_1050.m4s
        #EXTINF:2.000,
        seg_1051.m4s
        #EXTINF:2.000,
        seg_1052.m4s
        """;

        var segments = new List<NicoSegmentItem>();
        NicoHlsSession.ParseVariantPlaylist(variantM3u8, "https://example.com/live/720p/playlist.m3u8",
            out var initUrl, out var keyUrl, out var keyIv, segments);

        Assert.Equal("https://example.com/live/720p/init.mp4", initUrl);
        Assert.Equal("https://example.com/key", keyUrl);
        Assert.Equal("0x0000000000000000000000000000041a", keyIv);
        Assert.Equal(3, segments.Count);
        Assert.Equal("https://example.com/live/720p/seg_1050.m4s", segments[0].Url);
        Assert.Equal(2.0, segments[0].Duration);
    }

    [Fact]
    public void Test_ParseIv_And_DecryptAes128()
    {
        // 1. Explicit IV with 0x prefix
        var ivHex = "0x0123456789ABCDEF0123456789ABCDEF";
        var iv1 = NicoHlsSession.ParseIv(ivHex, 100);
        Assert.Equal(16, iv1.Length);
        Assert.Equal(0x01, iv1[0]);
        Assert.Equal(0xEF, iv1[15]);

        // 2. Fallback IV from sequence number (Big-Endian uint64 at offset 8)
        var iv2 = NicoHlsSession.ParseIv(null, 42);
        Assert.Equal(16, iv2.Length);
        Assert.Equal(42, iv2[15]);
        Assert.Equal(0, iv2[0]);

        // 3. Encrypt and Decrypt test
        var key = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var plaintext = "Hello NicoLive HLS stream encryption test!"u8.ToArray();

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        aes.IV = iv1;
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        var ciphertext = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);

        var decrypted = NicoHlsSession.DecryptAes128(ciphertext, key, iv1);
        Assert.Equal(plaintext, decrypted);
    }
}
