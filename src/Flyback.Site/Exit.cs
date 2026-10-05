namespace Flyback.Site;

/// <summary>What the program tells the shell it did.</summary>
internal static class Exit
{
    public const int Ok = 0;

    /// <summary>The file was read and refused.</summary>
    public const int Refused = 1;

    /// <summary>The job could not be done at all: a file missing, the site not answering.</summary>
    public const int Failed = 2;
}
