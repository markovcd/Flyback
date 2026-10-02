using Flyback.Editor.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Notices;

/// <summary>Registers a part of the editor with the reactions its class declares (ADR-0150).</summary>
internal static class Parts
{
    /// <summary>
    /// The interfaces a part is also reached as whenever it implements them, so a
    /// constructor can ask for all of them; an open generic stands for each of its closings.
    /// </summary>
    private static readonly Type[] Collected = [typeof(IReactTo<>), typeof(IReactTo), typeof(ISettingsSection)];

    /// <summary>
    /// One singleton of <typeparamref name="T"/>, reached as itself and as every
    /// <see cref="Collected"/> interface it implements.
    /// </summary>
    public static void AddPart<T>(this IServiceCollection services) where T : class
    {
        services.AddSingleton<T>();

        foreach (var face in CollectedBy(typeof(T)))
            services.AddSingleton(face, provider => provider.GetRequiredService<T>());
    }

    /// <summary>Every <see cref="Collected"/> interface a part implements.</summary>
    private static IEnumerable<Type> CollectedBy(Type part) =>
        part.GetInterfaces().Where(i => Collected.Contains(i.IsGenericType ? i.GetGenericTypeDefinition() : i));
}
