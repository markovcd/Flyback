# Architecture decision records

Each record captures one decision, the situation that forced it, and what it
costs. Format is [Michael Nygard's](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
context, decision, consequences.

Two records — [0003](0003-cpu-rendering-with-a-gpu-path-left-open.md) and
[0004](0004-visual-patch-editor-as-the-authoring-model.md) — are marked
**user-directed**: they were chosen by the project owner, not derived. They sit
upstream of almost everything else here, so they are recorded even though they
were not mine to make.

## Index

### Scope and shape

| # | Decision |
|---|---|
| [0001](0001-target-net-10.md) | Target .NET 10 and current C# |
| [0002](0002-split-engine-from-shell.md) | Split the engine from the UI shell |
| [0003](0003-cpu-rendering-with-a-gpu-path-left-open.md) | Render on the CPU, leave a GPU backend possible *(user-directed)* |
| [0004](0004-visual-patch-editor-as-the-authoring-model.md) | Author patches in a visual node editor *(user-directed)* |

### The engine

| # | Decision |
|---|---|
| [0005](0005-compile-to-a-flat-register-machine.md) | Compile patches to a flat register machine |
| [0006](0006-scalar-interpreter-parallel-over-rows.md) | Scalar interpreter parallelised over rows, not SIMD |
| [0007](0007-register-slots-with-scalar-broadcast.md) | Values are register slots; scalars broadcast to colours |
| [0008](0008-modules-as-data-in-one-catalogue.md) | Modules are data in a single catalogue |
| [0009](0009-editable-defaults-on-every-input.md) | Every input port carries an editable default |
| [0010](0010-any-typed-ports-for-polymorphic-maths.md) | `Any`-typed ports make maths modules polymorphic |
| [0011](0011-compile-backwards-from-output.md) | Compile backwards from the Output node |
| [0012](0012-feedback-as-a-module-not-a-cycle.md) | Feedback is an explicit module, not a graph cycle |
| [0013](0013-guard-arithmetic-instead-of-propagating-nan.md) | Guard arithmetic instead of propagating NaN |
| [0014](0014-coordinate-and-value-conventions.md) | Coordinate and value conventions |
| [0021](0021-recompile-the-whole-patch-on-every-edit.md) | Recompile the whole patch on every edit |

### The shell

| # | Decision |
|---|---|
| [0015](0015-avalonia-for-the-ui-shell.md) | Avalonia for the UI shell |
| [0016](0016-build-the-ui-in-c-sharp-without-xaml.md) | Build the UI in C#, without XAML |
| [0017](0017-draw-the-node-editor-in-one-control.md) | Draw the node editor in one custom control |
| [0018](0018-never-render-frames-on-the-ui-thread.md) | Never render frames on the UI thread |

### Boundaries

| # | Decision |
|---|---|
| [0019](0019-no-third-party-dependencies-in-the-engine.md) | No third-party dependencies in the engine |
| [0020](0020-json-patch-files-keyed-by-string-type-ids.md) | JSON patch files keyed by string type IDs |
