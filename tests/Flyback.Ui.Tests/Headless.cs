using Avalonia.Headless;
using Flyback.Ui.Testing;
using Flyback.Ui.Testing.Headless;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AvaloniaTestApplication(typeof(UiTestApp))]
[assembly: Parallelization(Mode = ParallelMode.Collections)]
[assembly: AssemblyFixture(typeof(PoolHeadroom))]
[assembly: AssemblyFixture(typeof(SessionFirst))]
