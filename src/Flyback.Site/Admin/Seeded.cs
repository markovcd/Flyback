namespace Flyback.Site.Admin;

/// <summary>What the site did with a default: added, replaced, unchanged, or deleted by the admin and left so.</summary>
internal sealed record Seeded(string Id, string State);
