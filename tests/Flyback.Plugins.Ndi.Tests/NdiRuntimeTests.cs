using System.Runtime.InteropServices;
using Flyback.Plugins.Ndi;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Ndi.Tests;

/// <summary>Where the NDI runtime is looked for: where NDI's installers put it, and nowhere a search path would find.</summary>
public class NdiRuntimeTests
{
    [Fact]
    public void On_Windows_only_the_folder_the_installer_named_is_looked_in()
    {
        NdiRuntime.Candidates(@"C:\Program Files\NDI\NDI 6 Runtime\v6", OSPlatform.Windows, Architecture.X64)
            .ShouldBe([@"C:\Program Files\NDI\NDI 6 Runtime\v6\Processing.NDI.Lib.x64.dll"]);
    }

    [Fact]
    public void With_no_folder_named_Windows_has_nowhere_to_look() =>
        NdiRuntime.Candidates(null, OSPlatform.Windows, Architecture.X64).ShouldBeEmpty();

    [Fact]
    public void A_folder_named_relatively_is_not_looked_in() =>
        NdiRuntime.Candidates("ndi", OSPlatform.Windows, Architecture.X64).ShouldBeEmpty();

    [Fact]
    public void On_a_Mac_the_runtime_s_and_the_SDK_s_folders_are_looked_in() =>
        NdiRuntime.Candidates(null, OSPlatform.OSX, Architecture.Arm64)
            .ShouldBe(["/usr/local/lib/libndi.dylib", "/Library/NDI SDK for Apple/lib/macOS/libndi.dylib"]);

    [Fact]
    public void On_Linux_the_system_s_library_folders_are_looked_in_by_soname() =>
        NdiRuntime.Candidates(null, OSPlatform.Linux, Architecture.X64)
            .ShouldBe(["/usr/lib/x86_64-linux-gnu/libndi.so.6", "/usr/lib/libndi.so.6", "/usr/local/lib/libndi.so.6"]);

    [Fact]
    public void A_Windows_processor_NDI_has_no_runtime_for_is_not_looked_for() =>
        NdiRuntime.Candidates(@"C:\NDI", OSPlatform.Windows, Architecture.Arm64).ShouldBeEmpty();
}
