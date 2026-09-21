using Xunit.Sdk;
using Xunit.v3;

namespace Avalonia.Headless.XUnit;

internal sealed class AvaloniaTestCaseRunnerContext(
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
    FixtureMappingManager fixtureMappings,
    HeadlessUnitTestSession session)
    : XunitTestCaseRunnerContext(
        testCase,
        tests,
        explicitOption,
        messageBus,
        aggregator,
        displayName,
        skipReason,
        cancellationTokenSource,
        parallelMode,
        scheduler,
        constructorArguments,
        fixtureMappings)
{
    public HeadlessUnitTestSession Session { get; } = session;
}
