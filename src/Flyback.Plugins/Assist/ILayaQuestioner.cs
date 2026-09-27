namespace Flyback.Plugins.Assist;

/// <summary>Answers bounded questions with a local Laya-compatible model.</summary>
public interface ILayaQuestioner
{
    /// <summary>Runs the request against the named installed local model.</summary>
    Task<LayaResponse> AskAsync(
        string modelId,
        LayaRequest request,
        CancellationToken cancellationToken = default);
}
