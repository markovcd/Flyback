using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Feedback()
    {
        yield return new NodeDef(
            "feedback", "Feedback", ModuleCategories.Feedback,
            [..Position()], [Col("color")],
            (em, i) => [em.Triple(OpCode.SampleFeedback, i[0], i[1])],
            "Reads the previous frame. Feed it back through space transforms to make a self-referential loop.")
        {
            Sinks = ModuleSinks.Video,
        };
    }
}
