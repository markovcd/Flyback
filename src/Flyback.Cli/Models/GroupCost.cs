namespace Flyback.Cli.Models;

/// <summary>What one group adds to a patch's two programs.</summary>
/// <param name="Name">The group's name, or what it is called while it has none.</param>
/// <param name="Modules">How many modules are inside.</param>
/// <param name="Picture">Ops the picture's program loses with the group switched off.</param>
/// <param name="Sound">Ops the sound's program loses with the group switched off.</param>
internal sealed record GroupCost(Guid Id, string Name, int Modules, int Picture, int Sound);
