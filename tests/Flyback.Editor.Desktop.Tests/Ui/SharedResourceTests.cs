using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Canvas;
using Flyback.Viewer.Desktop;
using Shouldly;

namespace Flyback.Editor.Desktop.Tests.Ui;

/// <summary>
/// What the shell keeps in a static is shared by every window and every thread, so
/// none of it may belong to one.
/// </summary>
/// <remarks>
/// An <see cref="AvaloniaObject"/> belongs to the thread it was made on. A mutable
/// brush or pen made in a type initializer that a background thread happened to run
/// first throws on the UI thread the next time anything draws with it. Immutable
/// brushes and pens belong to no thread; a cache of objects that must belong to one
/// is <see cref="ThreadStaticAttribute"/>.
/// </remarks>
public sealed class SharedResourceTests
{
    private static readonly Assembly[] Shell =
    [
        typeof(FlybackApp).Assembly,
        typeof(NodeEditor).Assembly,
        typeof(Flyback.Ui.Controls.Colors).Assembly,
        typeof(ViewerApp).Assembly,
    ];

    // On the UI thread, so a field this finds is not handed to a pool thread by the finding.
    [AvaloniaFact]
    public void No_static_holds_an_object_a_thread_owns()
    {
        var owned =
            from assembly in Shell
            from type in assembly.GetTypes()
            where !type.ContainsGenericParameters
            from field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            where !field.IsLiteral && field.GetCustomAttribute<ThreadStaticAttribute>() is null
            where Held(field.GetValue(null)).Any(value => value is AvaloniaObject)
            select $"{type.FullName}.{field.Name}";

        owned.ShouldBeEmpty("a static is shared across threads, and each of these belongs to the thread that made it");
    }

    /// <summary>The value, and what it holds where it is a collection.</summary>
    private static IEnumerable<object?> Held(object? value) => value switch
    {
        AvaloniaObject => [value],
        IDictionary map => map.Values.Cast<object?>(),
        IEnumerable items and not string => items.Cast<object?>(),
        _ => [value],
    };
}
