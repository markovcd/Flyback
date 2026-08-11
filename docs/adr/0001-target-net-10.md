# ADR-0001: Target .NET 10 and current C#

**Status:** Accepted · 2026-08-11

## Context

The brief asked for "modern .NET and C#". The build machine had SDKs 8.0.101,
9.0.102 and 10.0.400 installed, with `Microsoft.WindowsDesktop.App` runtimes for
all three. Nothing in the project constrains the floor: there is no library to
stay compatible with, no deployment target dictating an LTS version, and no
existing code to migrate.

## Decision

Target `net10.0` across both projects, with `LangVersion` set to `latest`,
nullable reference types on, and implicit usings on. These are set once in
`Directory.Build.props` rather than per project.

## Consequences

Collection expressions (`[..]`), primary constructors, `required` members and
list patterns are all available, and the code uses them heavily — `NodeCatalog`
in particular reads as a data table because of collection expressions.
`System.Text.Json` supports `required` and `init` members natively, which is why
patch serialisation needs no custom converters ([0020](0020-json-patch-files-keyed-by-string-type-ids.md)).

.NET 10 is not an LTS release. If this ever ships to machines that are not the
author's, that is worth revisiting — but the engine uses nothing version-specific
beyond syntax, so dropping to `net8.0` would be a `TargetFramework` edit plus
whatever the C# compiler objects to.

`net10.0` is not a Windows-specific target framework. That is deliberate: the
engine is portable, and Avalonia ([0015](0015-avalonia-for-the-ui-shell.md))
does not require a `-windows` TFM the way WPF or WinUI would.
