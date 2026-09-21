using Avalonia.Threading;
using Xunit.Sdk;
using Xunit.v3;

namespace Avalonia.Headless.XUnit;

internal sealed class AvaloniaTestRunner : XunitTestRunnerBase<AvaloniaTestRunnerContext, IXunitTest>
{
    public static AvaloniaTestRunner Instance { get; } = new();

    private AvaloniaTestRunner()
    {
    }

    public async ValueTask<RunSummary> Run(
        IXunitTest test,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExplicitOption explicitOption,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        ParallelMode parallelMode,
        ExecutionScheduler scheduler,
        IReadOnlyCollection<IBeforeAfterTestAttribute> beforeAfterAttributes,
        FixtureMappingManager fixtureMappings,
        HeadlessUnitTestSession session)
    {
        await using var ctxt = new AvaloniaTestRunnerContext(
            test,
            messageBus,
            explicitOption,
            aggregator,
            cancellationTokenSource,
            parallelMode,
            scheduler,
            beforeAfterAttributes,
            constructorArguments,
            fixtureMappings,
            session
        );
        await ctxt.InitializeAsync();

        return await session.Dispatch(
            async () =>
            {
                var dispatcher = Dispatcher.UIThread;
                var summary = await Run(ctxt);
                dispatcher.RunJobs();
                return summary;
            },
            ctxt.CancellationTokenSource.Token);
    }
}
