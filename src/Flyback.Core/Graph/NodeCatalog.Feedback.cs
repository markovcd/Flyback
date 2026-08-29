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
            "Reads the previous frame. Feed it back through space transforms to make a self-referential loop.");

        yield return new NodeDef(
            UnitDelayTypeId, "Unit Delay", "Feedback",
            [Num("in")], [Num("out")],

            // Never called. The compiler recognises a cycle breaker and lowers
            // it itself, into a read here and a write after every read in the
            // program. A wire is what it would be if that ever stopped being
            // true, which is the most harmless thing to be wrong about.
            (_, i) => [i[0]],
            "A one-sample delay for loops. Put it in a cycle to make downstream values read the previous evaluation.")
        {
            IsCycleBreaker = true,
        };
    }
}