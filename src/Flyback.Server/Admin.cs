using System.Security.Cryptography;
using System.Text;

namespace Flyback.Server;

/// <summary>The one admin account, named by the container's configuration.</summary>
/// <remarks>Admin mode is off unless both a user and a password are set.</remarks>
internal sealed class Admin(string? user, string? password)
{
    private readonly string? password = password;

    public bool Enabled { get; } = !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password);

    public string? User { get; } = user;

    public bool Accepts(string? givenUser, string? givenPassword) =>
        Enabled & Same(givenUser, User) & Same(givenPassword, password);

    /// <summary>Compared as hashes, so neither the length nor the first wrong character shows in the time taken.</summary>
    private static bool Same(string? given, string? expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given ?? string.Empty)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? string.Empty)));
}
