using System.Reflection;
using System.Reflection.Emit;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests;

/// <summary>
/// The editor's folders layer. The footing the features stand on names nothing above
/// itself, each feature names only the features below it, and nothing but the
/// composition names the composition.
/// </summary>
/// <remarks>
/// Read from the compiled editor rather than its <c>using</c> lines, since a child
/// namespace sees its parent's types, and a sibling's through its short name, with none.
/// </remarks>
[TestCategory(TestCategory.Unmeasured)]
public class FolderLayeringTests
{
    /// <summary>The folders under the features, lowest first; the root namespace is the lowest.</summary>
    private static readonly string[] Footing = ["", "Controls", "Updates", "Statistics", "Shell", "Notices", "Settings"];

    /// <summary>The features, lowest first; each names only the footing and the features below it.</summary>
    private static readonly string[] Features =
        ["Site", "Decide", "Midi", "Canvas", "Hubs", "Assist", "Knobs", "PluginPackages", "Capture", "Files", "Gallery", "Inspect", "Bars", "Keys", "Windows"];

    private const string Composition = "Composition";

    private static readonly Assembly Editor = typeof(ReportLine).Assembly;

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(c => c.Value);

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Fact]
    public void A_folder_names_no_folder_above_it()
    {
        var crossings =
            from type in Editor.GetTypes()
            let home = Folder(type)
            where home is not null
            from used in Named(type)
            let there = Folder(used)
            where there is not null && !Allowed(home, there)
            select $"{Name(type)} -> {Name(used)}";

        crossings.Distinct().Order().ShouldBeEmpty("a folder names only what is below it, and the composition is named by nothing");
    }

    private static bool Allowed(string from, string to) => from == to || Rank(to) < Rank(from);

    /// <summary>A footing folder's place in it, then a feature's place above those, then the composition.</summary>
    private static int Rank(string folder) =>
        folder == Composition ? Footing.Length + Features.Length
        : Array.IndexOf(Footing, folder) is >= 0 and var footing ? footing
        : Array.IndexOf(Features, folder) is >= 0 and var feature ? Footing.Length + feature
        : throw new ArgumentException($"{folder} is in neither the footing nor the features", nameof(folder));

    /// <summary>The folder an editor type is declared in, the root namespace being "", or null for any other type.</summary>
    private static string? Folder(Type type)
    {
        if (type.IsGenericParameter || type.Assembly != Editor || type.Namespace is not { } space) return null;
        if (space == "Flyback.Editor") return "";
        return space.StartsWith("Flyback.Editor.", StringComparison.Ordinal) ? space["Flyback.Editor.".Length..].Split('.')[0] : null;
    }

    private static string Name(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return Folder(type) is "" ? type.Name : $"{Folder(type)}.{type.Name}";
    }

    /// <summary>Every type a type's shape or its methods' bodies name.</summary>
    private static IEnumerable<Type> Named(Type type)
    {
        var named = new List<Type>();
        if (type.BaseType is { } baseType) named.Add(baseType);
        named.AddRange(type.GetInterfaces());
        named.AddRange(type.GetFields(Declared).Select(f => f.FieldType));
        named.AddRange(type.GetProperties(Declared).Select(p => p.PropertyType));
        named.AddRange(type.GetEvents(Declared).Select(e => e.EventHandlerType!));

        foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
        {
            if (method is MethodInfo info) named.Add(info.ReturnType);
            named.AddRange(method.GetParameters().Select(p => p.ParameterType));
            if (method.GetMethodBody() is not { } body) continue;

            named.AddRange(body.LocalVariables.Select(l => l.LocalType));
            named.AddRange(Body(method, body.GetILAsByteArray()!));
        }

        return named.SelectMany(Within);
    }

    /// <summary>The types the instructions of a method's body take as operands.</summary>
    private static IEnumerable<Type> Body(MethodBase method, byte[] il)
    {
        var typeArguments = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (var at = 0; at < il.Length;)
        {
            var code = Codes[il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at]];
            at += code.Size;

            if (code.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
            {
                switch (method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments))
                {
                    case Type used:
                        yield return used;
                        break;
                    case FieldInfo field:
                        yield return field.DeclaringType!;
                        yield return field.FieldType;
                        break;
                    case MethodBase called:
                        yield return called.DeclaringType!;
                        if (called is MethodInfo info) yield return info.ReturnType;
                        foreach (var parameter in called.GetParameters()) yield return parameter.ParameterType;
                        if (called.IsGenericMethod)
                            foreach (var argument in called.GetGenericArguments()) yield return argument;
                        break;
                }
            }

            at += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                _ => 4,
            };
        }
    }

    /// <summary>A type and every type it is built from: its elements and its type arguments.</summary>
    private static IEnumerable<Type> Within(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var inner in Within(type.GetElementType()!)) yield return inner;
            yield break;
        }

        yield return type;

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            foreach (var argument in type.GetGenericArguments())
                foreach (var inner in Within(argument))
                    yield return inner;
    }
}
