namespace Flyback.Plugins.Hosting;

/// <summary>Somebody's yes to one plugin folder, as it stood when they said it.</summary>
/// <param name="Folder">The folder's full path.</param>
/// <param name="Assembly">The plugin's assembly, by name, for a person reading the list.</param>
/// <param name="Signer">The key its package was signed with, or null for a folder no signed package filled.</param>
/// <param name="Secrets">Whether it may register a secret store.</param>
/// <param name="Files">What was in the folder; a file changed since is a folder nobody said yes to.</param>
internal sealed record PluginAllowance(string Folder, string Assembly, string? Signer, bool Secrets, PluginFiles Files);
