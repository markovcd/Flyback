using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Everything in an assembly that a plugin could have been compiled against,
/// written out one member to a line so that a change to it is a change to a
/// text file.
/// </summary>
/// <remarks>
/// What is written is what the compiler puts into a plugin, which is more than
/// signatures: the number behind an enum member, the value of a constant and the
/// default of an optional parameter are all copied into the caller, so each is
/// on the line beside the thing it belongs to. What a record synthesizes is left
/// out, since it follows from the parameters and properties that are there.
/// </remarks>
internal static class ContractSurface
{
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void", [typeof(object)] = "object", [typeof(string)] = "string",
        [typeof(bool)] = "bool", [typeof(char)] = "char", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
        [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(int)] = "int", [typeof(uint)] = "uint",
        [typeof(long)] = "long", [typeof(ulong)] = "ulong", [typeof(float)] = "float",
        [typeof(double)] = "double", [typeof(decimal)] = "decimal",
    };

    public static string Of(Assembly assembly)
    {
        var text = new StringBuilder();

        foreach (var type in assembly.GetTypes().Where(Visible).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            text.Append(Header(type)).Append('\n');

            foreach (var line in Members(type)) text.Append("    ").Append(line).Append('\n');

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Public all the way out, since a public type nested in an internal one cannot be named.</summary>
    private static bool Visible(Type type) =>
        type.IsNested
            ? (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem) && Visible(type.DeclaringType!)
            : type.IsPublic;

    private static bool Visible(MethodBase? method) =>
        method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static bool Synthesized(MemberInfo member) =>
        member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$", Declared) is not null
        || (type.IsValueType && type.GetMethod("PrintMembers", Declared) is { } print && Synthesized(print));

    private static string Header(Type type)
    {
        var name = type.Namespace + "." + Name(type, qualified: true);

        if (type.IsEnum) return $"enum {name} : {Name(Enum.GetUnderlyingType(type))}";

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            var invoke = type.GetMethod("Invoke")!;
            return $"delegate {name}({Parameters(invoke)}) : {Name(invoke.ReturnType)}";
        }

        var kind = type switch
        {
            { IsInterface: true } => "interface",
            { IsValueType: true } => (type.IsDefined(typeof(IsReadOnlyAttribute), false) ? "readonly " : "")
                + (IsRecord(type) ? "record struct" : "struct"),
            { IsAbstract: true, IsSealed: true } => "static class",
            _ => (type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : "") + (IsRecord(type) ? "record" : "class"),
        };

        var inherited = type.BaseType?.GetInterfaces() ?? [];

        var bases = new List<string>();

        if (type.BaseType is { } parent && parent != typeof(object) && parent != typeof(ValueType))
            bases.Add(Name(parent));

        bases.AddRange(type.GetInterfaces()
            .Except(inherited)
            .Where(i => !(IsRecord(type) && i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEquatable<>)))
            .Select(i => Name(i))
            .Order(StringComparer.Ordinal));

        return $"{kind} {name}{Constraints(type.GetGenericArguments())}" + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (typeof(Delegate).IsAssignableFrom(type)) return [];

        if (type.IsEnum)
        {
            return Enum.GetNames(type)
                .Select(n => (Name: n, Value: Convert.ToInt64(Enum.Parse(type, n), CultureInfo.InvariantCulture)))
                .OrderBy(m => m.Value)
                .Select(m => $"{m.Name} = {m.Value}");
        }

        var fields = type.GetFields(Declared)
            .Where(f => (f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly) && !Synthesized(f))
            .Select(Field);

        var constructors = type.GetConstructors(Declared)
            .Where(c => Visible(c) && !c.IsStatic && !Synthesized(c) && !(type.IsAbstract && type.IsSealed))
            .Where(c => !(IsRecord(type) && c.GetParameters() is [{ } only] && only.ParameterType == type))
            .Select(c => $"new({Parameters(c)})");

        var properties = type.GetProperties(Declared)
            .Where(p => (Visible(p.GetMethod) || Visible(p.SetMethod)) && p.Name != "EqualityContract")
            .Select(Property);

        var events = type.GetEvents(Declared)
            .Where(e => Visible(e.AddMethod))
            .Select(e => $"event {Modifiers(e.AddMethod!)}{e.Name} : {Name(e.EventHandlerType!)}");

        var methods = type.GetMethods(Declared)
            .Where(m => Visible(m) && !Synthesized(m) && !Accessor(m))
            .Select(Method);

        return [.. Sorted(fields), .. Sorted(constructors), .. Sorted(properties), .. Sorted(events), .. Sorted(methods)];
    }

    private static IEnumerable<string> Sorted(IEnumerable<string> lines) => lines.Order(StringComparer.Ordinal);

    private static bool Accessor(MethodInfo method) =>
        method.IsSpecialName && AccessorPrefixes.Any(prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal));

    private static readonly string[] AccessorPrefixes = ["get_", "set_", "add_", "remove_"];

    private static string Field(FieldInfo field)
    {
        var access = field.IsPublic ? "" : "protected ";

        if (field.IsLiteral)
            return $"{access}const {field.Name} : {Name(field.FieldType)} = {Literal(field.GetRawConstantValue(), field.FieldType)}";

        return $"{access}{(field.IsStatic ? "static " : "")}{(field.IsInitOnly ? "readonly " : "")}{field.Name} : {Name(field.FieldType)}";
    }

    private static string Property(PropertyInfo property)
    {
        var lead = property.GetMethod ?? property.SetMethod!;
        var accessors = new List<string>();

        if (Visible(property.GetMethod)) accessors.Add(Access(property.GetMethod!, lead) + "get;");

        if (Visible(property.SetMethod))
        {
            var init = property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
            accessors.Add(Access(property.SetMethod, lead) + (init ? "init;" : "set;"));
        }

        var index = property.GetIndexParameters();
        var name = index.Length > 0 ? $"this[{Parameters(index)}]" : property.Name;

        return $"{Modifiers(lead)}{name} {{ {string.Join(" ", accessors)} }} : {Name(property.PropertyType)}";
    }

    /// <summary>Said only on the accessor that is narrower than the property it belongs to.</summary>
    private static string Access(MethodInfo accessor, MethodInfo lead) =>
        !accessor.IsPublic && lead.IsPublic ? "protected " : "";

    private static string Method(MethodInfo method)
    {
        var generic = method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
            : "";

        var constraints = method.IsGenericMethodDefinition ? Constraints(method.GetGenericArguments()) : "";

        return $"{Modifiers(method)}{method.Name}{generic}({Parameters(method)}) : {Name(method.ReturnType)}{constraints}";
    }

    private static string Modifiers(MethodBase method)
    {
        var access = method.IsPublic ? "" : "protected ";

        if (method.IsStatic) return access + "static ";

        // On an interface, abstract is the ordinary case and goes unsaid; what
        // matters is the member that has a body, because that is the only kind
        // that can be added without breaking whoever already implements it.
        if (method.DeclaringType!.IsInterface) return access + (method.IsAbstract ? "" : "default ");

        if (method.IsAbstract) return access + "abstract ";

        if (method is MethodInfo { IsVirtual: true, IsFinal: false } m)
            return access + (m.GetBaseDefinition() != m ? "override " : "virtual ");

        return access;
    }

    private static string Parameters(MethodBase method) => Parameters(method.GetParameters());

    private static string Parameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(Parameter));

    private static string Parameter(ParameterInfo parameter)
    {
        var passing = parameter switch
        {
            { IsOut: true } => "out ",
            { IsIn: true } => "in ",
            { ParameterType.IsByRef: true } => "ref ",
            _ when parameter.IsDefined(typeof(ParamArrayAttribute), false) => "params ",
            _ => "",
        };

        var value = parameter.HasDefaultValue ? " = " + Literal(parameter.RawDefaultValue, parameter.ParameterType) : "";

        return $"{passing}{Name(parameter.ParameterType)} {parameter.Name}{value}";
    }

    private static string Constraints(Type[] arguments)
    {
        var text = new StringBuilder();

        foreach (var argument in arguments.Where(a => a.IsGenericParameter))
        {
            var attributes = argument.GenericParameterAttributes;
            var parts = new List<string>();

            if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) parts.Add("class");
            if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add("struct");

            parts.AddRange(argument.GetGenericParameterConstraints()
                .Where(c => c != typeof(ValueType))
                .Select(c => Name(c))
                .Order(StringComparer.Ordinal));

            if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint)
                && !attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
                parts.Add("new()");

            if (parts.Count > 0) text.Append($" where {argument.Name} : {string.Join(", ", parts)}");
        }

        return text.ToString();
    }

    /// <summary>
    /// A type as it would be written, without its namespace: the header of each
    /// type carries that, and a line that named every namespace in full would be
    /// mostly <c>System.Collections.Generic</c>.
    /// </summary>
    private static string Name(Type type, bool qualified = false)
    {
        if (type.IsByRef) return Name(type.GetElementType()!);
        if (type.IsArray) return Name(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsPointer) return Name(type.GetElementType()!) + "*";
        if (type.IsGenericParameter) return type.Name;
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Name(underlying) + "?";
        if (Keywords.TryGetValue(type, out var keyword)) return keyword;

        var name = type.Name;
        var arguments = type.GetGenericArguments();

        if (name.IndexOf('`') is var tick and >= 0)
        {
            var arity = int.Parse(name[(tick + 1)..], CultureInfo.InvariantCulture);
            name = name[..tick] + "<" + string.Join(", ", arguments[^arity..].Select(a => Name(a))) + ">";
        }

        return type.IsNested && !type.IsGenericParameter ? Name(type.DeclaringType!, qualified) + "." + name : name;
    }

    private static string Literal(object? value, Type type)
    {
        if (value is null or DBNull) return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? "default" : "null";

        var plain = Nullable.GetUnderlyingType(type) ?? type;

        if (plain.IsEnum)
            return Enum.IsDefined(plain, value) ? $"{plain.Name}.{Enum.GetName(plain, value)}" : $"({plain.Name}){value}";

        return value switch
        {
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"",
            char c => $"'{c}'",
            bool b => b ? "true" : "false",
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
    }
}
