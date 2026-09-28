using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App.Notices;

/// <summary>Registers a part of the editor with the reactions its class declares (ADR-0150).</summary>
internal static class Parts
{
    /// <summary>
    /// One singleton of <typeparamref name="T"/>, reached as itself and as the reactor to
    /// every notice it implements <see cref="IReactTo{T}"/> for.
    /// </summary>
    public static IServiceCollection AddPart<T>(this IServiceCollection services) where T : class
    {
        services.AddSingleton<T>();

        foreach (var reaction in ReactionsOf(typeof(T)))
        {
            services.AddSingleton(reaction, provider => provider.GetRequiredService<T>());
            services.AddSingleton(new DeclaredReaction(reaction));
        }

        return services;
    }

    /// <summary>Every <see cref="IReactTo{T}"/> a part implements.</summary>
    public static IEnumerable<Type> ReactionsOf(Type part) =>
        part.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReactTo<>));
}
