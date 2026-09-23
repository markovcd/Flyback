using Flyback.Plugins.Hosting;

namespace Flyback.Server;

/// <summary>
/// The release signing key a run from the source signs with: <c>RELEASE_SIGNING_KEY</c>,
/// the variable the Release workflow reads its secret into, holding a local test key here.
/// </summary>
internal static class ReleaseKey
{
    public const string Variable = "RELEASE_SIGNING_KEY";

    /// <summary>
    /// The key kept in the user's environment, for a process started before it was put there.
    /// </summary>
    public static string? Kept() =>
        OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(Variable, EnvironmentVariableTarget.User) is { Length: > 0 } pem
            ? pem
            : null;

    /// <summary>A new key, kept in the user's environment on Windows and for this run elsewhere.</summary>
    public static string Make()
    {
        var pem = PackageSigner.NewKey();

        if (OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable(Variable, pem, EnvironmentVariableTarget.User);

        Environment.SetEnvironmentVariable(Variable, pem);

        return pem;
    }
}
