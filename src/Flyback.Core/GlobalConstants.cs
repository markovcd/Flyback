namespace Flyback.Core;

public static class GlobalConstants
{
    public const string ApplicationName = nameof(Flyback);
    public const int SampleRate = 48_000;

    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ApplicationName);
}