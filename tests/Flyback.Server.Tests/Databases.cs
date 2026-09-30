using Microsoft.Data.Sqlite;

namespace Flyback.Server.Tests;

internal static class Databases
{
    /// <summary>
    /// Closes the site's pooled connections to every database under <paramref name="folder"/>,
    /// so it can be deleted. Clearing every pool would close one a test running beside this
    /// one is in the middle of using.
    /// </summary>
    public static void Release(string folder)
    {
        foreach (var path in Directory.EnumerateFiles(folder, "*.db", SearchOption.AllDirectories))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString());
            SqliteConnection.ClearPool(connection);
        }
    }
}
