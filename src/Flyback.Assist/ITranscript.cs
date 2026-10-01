namespace Flyback.Assist;

/// <summary>Where a conversation is written as it happens: the editor's column, or a console.</summary>
internal interface ITranscript
{
    /// <summary>The lines kept, which are saved with the patch.</summary>
    IReadOnlyList<TranscriptLine> Lines { get; }

    bool IsEmpty { get; }

    void Clear();

    void Put(Spoken spoken);
}
