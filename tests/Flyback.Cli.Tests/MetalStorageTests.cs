using Flyback.Gpu;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>Where a Metal texture is kept, which runs on any machine since it opens no device.</summary>
public class MetalStorageTests
{
    [Fact]
    public void A_texture_only_the_GPU_touches_is_private()
    {
        MetalDevice.Storage(written: false, unified: false, mac: true).ShouldBe(MetalDevice.StoragePrivate);
        MetalDevice.Storage(written: false, unified: true, mac: false).ShouldBe(MetalDevice.StoragePrivate);
    }

    [Fact]
    public void A_written_texture_is_shared_where_memory_is_one()
    {
        MetalDevice.Storage(written: true, unified: true, mac: true).ShouldBe(MetalDevice.StorageShared);
    }

    [Fact]
    public void A_written_texture_is_managed_on_a_Mac_with_its_own_GPU_memory()
    {
        MetalDevice.Storage(written: true, unified: false, mac: true).ShouldBe(MetalDevice.StorageManaged);
    }

    /// <summary>The iOS simulator says its memory is not unified, and Metal aborts the app on a managed texture there.</summary>
    [Fact]
    public void A_written_texture_is_shared_on_iOS_whatever_the_memory()
    {
        MetalDevice.Storage(written: true, unified: false, mac: false).ShouldBe(MetalDevice.StorageShared);
    }
}
