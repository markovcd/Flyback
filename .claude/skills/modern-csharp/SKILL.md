---
name: modern-csharp
description: Use when writing or reviewing C# in src/ or tests/ - which C# 12 to 15 features new code reaches for (unions, closed hierarchies, extension members, field, collection expressions with with(...), labeled break), where each fits in Flyback, and the few that stay off.
---

# Modern C#

## Write the newest language the SDK ships

`Directory.Build.props` sets `<LangVersion>latest</LangVersion>`, which is the
newest stable C# of the SDK in `global.json`: C# 14 on the .NET 10 SDK. Never
`preview`, and never pin a number in a project file.

**Why:** the user asked for it outright. The newest features say more with
less code, and code written in an older dialect is drift the next reader has
to translate.

**How to apply:** new and changed code uses the forms below. Old code is left
alone unless the change already touches that member; rewriting a file into the
new dialect is a refactor to propose (`propose-refactors.md`), not something
to fold into a feature commit.

## C# 15, once the SDK is .NET 11

These do not compile on the .NET 10 SDK. Use them only after `global.json`
moves to .NET 11; until then they are what to reach for on that day.

**Union types** for a value that is exactly one of a few unrelated types. The
compiler checks a `switch` over it for exhaustiveness, so no `_ => throw`.

```csharp
public sealed record Loaded(string Path);
public sealed record Refused(string Reason);
public union Opened(Loaded, Refused);

string Say(Opened opened) => opened switch
{
    Loaded loaded => $"opened {loaded.Path}",
    Refused refused => refused.Reason,
};
```

Reach for one in place of a result class with a nullable field per outcome,
or an `object` that is "one of these". Not for a hierarchy that shares members:
that is a closed hierarchy.

**Closed hierarchies** for an abstract base whose cases all live in its own
assembly, such as `Expr`, `Statement`, `Value` and `Term` in the engine's
language. `closed` implies `abstract`, and every `switch` over it is checked
for exhaustiveness. An intermediate base is `closed` too, or checking stops
at it.

```csharp
public closed record Expr(int Line, int Column);
public sealed record Number(double Value, int Line, int Column) : Expr(Line, Column);
```

A `closed` type in Flyback.Core or Flyback.Plugins forbids plugins from
deriving, which moves the contract: load the `plugin-contract` skill first.

**Collection expression arguments** in place of `new X(comparer)` or a
capacity, then adds.

```csharp
HashSet<string> taken = [with(StringComparer.OrdinalIgnoreCase)];
List<Token> kept = [with(capacity: tokens.Count), .. tokens.Where(Kept)];
```

**Labeled `break` and `continue`** in place of a `goto`, a `found` flag or a
loop pulled into a method only to return out of it.

```csharp
rows: foreach (var row in grid)
    foreach (var cell in row)
        if (cell.Blocked) continue rows;
```

**Extension indexers** in an `extension` block when an indexer on a type
Flyback doesn't own reads better than a `ValueAt(i)` method.

## C# 14

**Extension blocks** for every new extension, and for an old `this` method the
change touches. Properties and static members go there too.

```csharp
public static class SpanReading
{
    extension(ReadOnlySpan<float> samples)
    {
        public float Last => samples[^1];
        public bool Silent => samples.IndexOfAnyExcept(0f) < 0;
    }
}
```

**`field`** in place of a backing field that only one property uses.

```csharp
public string Name { get; set => field = value.Trim(); } = "";
```

**Null-conditional assignment**: `inspector?.Selected = module;` in place of an
`if (inspector is not null)` around it.

**`nameof` of an unbound generic**: `nameof(List<>)`.

**Lambda parameter modifiers without types**:
`TryRead parse = (text, out value) => double.TryParse(text, out value);`

**Implicit span conversions**: pass an array straight to a `ReadOnlySpan<T>`
parameter. Take `ReadOnlySpan<T>` for a buffer that is only read.

**User-defined compound assignment** (`public void operator +=(T x)`) only
where an in-place update saves an allocation on the sound path; measure it
first (the `performance` skill).

## C# 12 and 13, still the default

- Primary constructors for services and small types; a parameter that is
  captured and never reassigned needs no field.
- Collection expressions (`[]`, `[a, .. rest]`) for every collection literal,
  `params ReadOnlySpan<T>` in place of `params T[]`.
- `System.Threading.Lock` for a new lock object, `\e` for the escape character.
- Raw string literals (`"""`) for multi-line text, GLSL and JSON in tests.

## Staying off

- `partial` beyond what a source generator completes, including C# 14's
  partial constructors and events (`one-type-per-file.md`).
- Anything that needs `<LangVersion>preview</LangVersion>`, such as C# 15's
  memory-safety rules and `unsafe(expression)`. Wait for it to ship.
