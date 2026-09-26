namespace Flyback.Core.Compile;

/// <summary>A stretch of a program as one method, taking what the interpreter's walk takes.</summary>
internal delegate void IlStage(
    ref double bank,
    double x,
    double y,
    double t,
    double aspect,
    ref FeedbackFrame feedback,
    DelayState? delays,
    LiveValues? live,
    Span<float> planes);