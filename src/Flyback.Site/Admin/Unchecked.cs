namespace Flyback.Site.Admin;

/// <summary>What the site says waits to be checked, as GET /api/v1/admin/unchecked lists it.</summary>
internal sealed record Unchecked(List<UncheckedFile> Presets, List<UncheckedFile> Plugins);
