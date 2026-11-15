using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace VRCVideoCacher.Tests;

/// <summary>
///     Runs before any test body and points the app at a throwaway data folder, so tests never read or
///     write the real config, database, thumbnails or cache under the user's profile.
/// </summary>
internal static class TestDataFolder
{
    private static readonly string Root = Path.Join(Path.GetTempPath(), $"vvc-tests-{Guid.NewGuid():N}");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("VVC_DATA_PATH", Root);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                // The pooled contexts keep the database file open until the pools are cleared.
                SqliteConnection.ClearAllPools();
                Directory.Delete(Root, true);
            }
            catch
            {
                // The temp folder is disposable; a leftover file must not fail the run.
            }
        };
    }
}
