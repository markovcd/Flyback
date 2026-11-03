namespace Flyback.Editor.Notices;

/// <summary>The sound device just saved plays at another rate than this run's, so only a restart puts it in force.</summary>
internal sealed record DeviceRateDiffers : INotice;
