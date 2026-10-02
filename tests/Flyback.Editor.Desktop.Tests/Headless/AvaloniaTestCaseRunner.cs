using Xunit.Sdk;
using Xunit.v3;

namespace Avalonia.Headless.XUnit;

internal sealed class AvaloniaTestCaseRunner
    : XunitTestCaseRunnerBase<AvaloniaTestCaseRunnerContext, IXunitTestCase, IXunitTest>
{
    public static AvaloniaTestCaseRunner Instance { get; } = new();

    private AvaloniaTestCaseRunner()
    {
    }

    public async ValueTask<RunSummary> Run(
        IXunitTestCase testCase,
        IReadOnlyCollection<IXunitTest> tests,
        IMessageBus messageBus,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        string displayName,
        string? skipReason,
        ExplicitOption explicitOption,
        ParallelMode parallelMode,
        ExecutionScheduler scheduler,
        object?[] constructorArguments,
        FixtureMappingManager fixtureMappings)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(testCase.TestClass.Class.Assembly);

        await using var ctxt = new AvaloniaTestCaseRunnerContext(
            testCase,
            tests,
            messageBus,
            aggregator,
            cancellationTokenSource,
            displayName,
            skipReason,
            explicitOption,
            parallelMode,
            scheduler,
            constructorArguments,
            fixtureMappings,
            session);
        await ctxt.InitializeAsync();

        return await Run(ctxt);
    }

    protected override ValueTask<RunSummary> RunTest(
        AvaloniaTestCaseRunnerContext ctxt,
        IXunitTest test)
        => AvaloniaTestRunner.Instance.Run(
            test,
            ctxt.MessageBus,
            ctxt.ConstructorArguments,
            ctxt.ExplicitOption,
            ctxt.Aggregator.Clone(),
            ctxt.CancellationTokenSource,
            ctxt.ParallelMode,
            ctxt.Scheduler,
            ctxt.BeforeAfterTestAttributes,
            ctxt.CaseFixtureMappings,
            ctxt.Session);
}
