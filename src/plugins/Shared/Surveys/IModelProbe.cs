namespace Flyback.Plugins.Surveys;

/// <summary>
/// One provider's side of a survey: where its catalog is and how it writes one
/// question. What is asked, in what order, and how the answers are read is
/// <see cref="SurveyLoop"/>'s.
/// </summary>
internal interface IModelProbe
{
    /// <summary>Every id the endpoint lists.</summary>
    Task<IReadOnlyList<string>> Catalog(IProgress<string>? said, CancellationToken cancel);

    /// <summary>Whether a listed model could build a patch.</summary>
    bool Candidate(string model);

    /// <summary>The ping, carrying a picture or a sound where one is given.</summary>
    Task<ProbeAnswer> Ask(string model, byte[]? picture, byte[]? sound, CancellationToken cancel);

    /// <summary>Whether a refused ping means the model takes nothing without a sound.</summary>
    bool OnlyHears(ProbeAnswer refused) => false;

    /// <summary>Whether a thinking budget can be measured here.</summary>
    bool Thinks => false;

    /// <summary>The smallest and largest thinking budget a model takes, where measured.</summary>
    Task<(int? Least, int? Most)> Bounds(string model, IProgress<string>? said, CancellationToken cancel) =>
        Task.FromResult<(int?, int?)>((null, null));
}
