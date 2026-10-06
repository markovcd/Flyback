namespace Flyback.Site.Admin;

/// <summary>Every checked preset's id and file name, as GET /api/v1/admin/presets lists them.</summary>
internal sealed record CheckedPresets(List<UncheckedFile> Items);
