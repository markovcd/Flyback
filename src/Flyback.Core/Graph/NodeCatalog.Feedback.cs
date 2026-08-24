using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>
    /// The one evaluation of delay a loop needs, and the module the editor puts on
    /// a wire that would otherwise close a cycle — see <see cref="Patch.WouldCycle"/>.
    /// </summary>
    public const string UnitDelayTypeId = "feedback.unit";
    
    private static IEnumerable<NodeDef> Feedback()
    {
        yield return new NodeDef(
            "feedback", "Feedback", "Feedback",
            [..Position()], [Col("color")],
            (em, i) => [em.Triple(OpCode.SampleFeedback, i[0], i[1])],
            "Samples the previous frame. Wire the output back towards Output through "
            + "a Rotate or Scale to get the classic camera-pointed-at-its-own-monitor loop.");

        yield return new NodeDef(
            UnitDelayTypeId, "Unit Delay", "Feedback",
            [Num("in")], [Num("out")],

            // Never called. The compiler recognises a cycle breaker and lowers
            // it itself, into a read here and a write after every read in the
            // program. A wire is what it would be if that ever stopped being
            // true, which is the most harmless thing to be wrong about.
            (_, i) => [i[0]],
            "One evaluation of delay, and the only module a wire may run backwards into. "
            + "Put one anywhere in a loop and the loop is legal: everything downstream of "
            + "it reads what came round last time rather than this time. That is what an "
            + "oscillator patched into its own phase needs, or a filter into its own input. "
            + "Audio only — a picture is drawn all at once, with no previous evaluation for "
            + "a pixel to read, so use Feedback for a loop you want to see.")
        {
            IsCycleBreaker = true,
        };
    }
}