# One type per file

## Split files that hold several types

A file declares one type, and the file is named for it: `PluginInstalls` lives in `PluginInstalls.cs`. When a change touches a file that declares more than one type, split it in the same change, one file per type, each named for its type, in the same folder and namespace.

**Why:** the user stated it as a standing rule. A type is found by its file name, and a second type tucked into another's file is not.

**How to apply:** the split covers every top-level type: classes, records, structs, enums, interfaces and delegates. A nested type stays inside its owner. A generic type and the non-generic type of the same name share one file, named for them both (`IReactTo<T>` and `IReactTo` in `IReactTo.cs`). Move each type verbatim with its own usings, then build; do not tidy it in the same commit. Leave files this session did not touch alone, however many types they hold.

## No partial types split across files

A type is written in one file. `partial` is only for what a source generator completes (`[LibraryImport]`, `[GeneratedRegex]`, `[JSImport]`, `[JSExport]`, `[JsonSerializable]`, generated test classes), and the hand-written half still sits in one file.

**Why:** the user stated it as a standing rule. A class cut into `Name.Concern.cs` files is still one class with one set of fields, so the cut hides its size instead of reducing it, and its state is shared across files nobody reads together.

**How to apply:** a class too big for one file is split by region into types of their own, each owning its state (`MainWindow`'s, ADR-0148). When a change touches a type spread across partial files, take it apart in the same change: pull each concern that owns state into its own type, and fold what is left into the one file. A test class spread across files becomes one test class per concern, each named for it. Never start a new partial file.
