using System.Collections.Generic;
using System.Threading;
using Xunit.Sdk;
using Xunit.v3;

namespace Avalonia.Headless.XUnit;

internal sealed class AvaloniaTestRunnerContext(
    IXunitTest test,
    IMessageBus messageBus,
    ExplicitOption explicitOption,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource,
    ParallelMode parallelMode,
    ExecutionScheduler scheduler,
    IReadOnlyCollection<IBeforeAfterTestAttribute> beforeAfterTestAttributes,
    object?[] constructorArguments,
    FixtureMappingManager fixtureMappings,
    HeadlessUnitTestSession session)
    : XunitTestRunnerContext(
        test,
        explicitOption,
        messageBus,
        aggregator,
        cancellationTokenSource,
        parallelMode,
        scheduler,
        beforeAfterTestAttributes,
        constructorArguments,
        fixtureMappings)
{
    public HeadlessUnitTestSession Session { get; } = session;
}
