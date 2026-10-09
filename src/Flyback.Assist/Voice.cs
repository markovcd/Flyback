namespace Flyback.Assist;

/// <summary>Whose a line of the transcript is, which decides how it is drawn.</summary>
internal enum Voice
{
    /// <summary>What the person asked.</summary>
    You,

    /// <summary>What the assistant said in words.</summary>
    Said,

    /// <summary>What it did, looked at or listened to, and what the panel says about the conversation.</summary>
    Note,

    /// <summary>The small print: what a turn cost, and what applying a proposal did.</summary>
    Aside,

    Proposed,

    Failed,

    /// <summary>Handbook text it looked up, shown only while the settings say so.</summary>
    Handbook,

    /// <summary>The briefing it was handed, shown only while the settings say so.</summary>
    Briefing,
}
