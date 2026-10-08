# A decision model that runs here: the Laya plugin

Planned on 2026-09-28; the rest of it landed on 2026-10-08, on `main` at `503a690a`. It is on
TODO.md; take it off there, and delete this file, in the commit that lands the Laya plugin.

## Where it stands

Landed ([ADR-0186](../adr/0186-a-decision-model-answers-typed-questions-behind-the-plugin-boundary.md)):
the contract in `src/Flyback.Plugins/Decide` (`IDecisionModel`, `IPreparedModel`,
`ModelFile`, the questions and answers, `SystemOneWire`), `Decisions` as the one door,
`ModelStore` downloading pinned files, `DecisionSettings` in the `decisions` section of
`settings.json`, the Jev plugin (`src/plugins/Flyback.Plugins.SystemOne`), `flyback-cli
decide`, Settings → Decisions, and the uses: the module list by meaning (`ModuleFinder`,
`modules --find`), complaints in order (`IssueTriage`, `check --triage`, the status line)
and the assistant's messages read before sending (`TurnReading`).

Until somebody chooses, questions go to the likeliest installed model that sends nothing
anywhere. Today none is installed, so nothing is asked until Jev is chosen. Laya is the
model that makes the default do something.

Left out of the landed work on purpose:

- The forgotten-proposal follow-up: `TurnLoop.Unproposed` already tells the assistant once.
- The workbench's `find_modules` by meaning: a tool answer is synchronous, and an assistant
  searches well enough by words.
- Senses off for a turn read as a question: the workbench decides senses per conversation,
  not per turn.

## What is left: Laya, in the box

Laya (Convai Innovations, Apache-2.0) answers in one forward pass, 200–460 ms on a CPU. The
plugin ships ONNX Runtime and a tokenizer; the host downloads the model itself, once, after
a yes, from a Hugging Face repo the maintainer owns.

### Once, by the maintainer: the export

`scripts/laya/export-onnx.ps1` runs a venv with laya 0.3.20, torch CPU and the English
checkpoint (on the Windows development machine under `laya-test/`, ignored) through
Laya's `scripts/export_onnx.py --quantize`: opset 18; inputs `input_ids`,
`attention_mask`, `marker_pos`, `marker_mask`, `qtype`; outputs `logits`,
`act_logits`; per-channel INT8, about 450 MB. The script also writes test fixtures (token
ids and marker positions for about 20 states and questions, and Python's answers for
them), prints SHA-256s and sizes, and says what to upload: `laya-int8.onnx`,
`tokenizer.json`, `rl_agent_config.json` and a README crediting
`convaiinnovations/laya`. The revision and the hashes go into `LayaFiles`. This is the
one Python step, on the maintainer's machine only, and the README's build section says so.

### The plugin: `src/plugins/Flyback.Plugins.Laya`

- `LayaPlugin`, and `LayaModel : IDecisionModel, IPreparedModel`: `Credential` and
  `Endpoint` null, `Priority` above Jev's 10, `Unavailable` only `File.Exists`, and the
  ONNX session built on the first `DecideAsync`, never at `Register`.
- `LayaFiles`: the three files with their pinned revision, sizes and SHA-256s.
- `LayaTokenizer`: `Microsoft.ML.Tokenizers` 2.0, `BpeTokenizer.Create(BpeOptions)` with
  `ByteLevel = true` and `PreTokenizer = RobertaPreTokenizer.Instance` (the GPT-2 regex
  the checkpoint's pre-tokenizer is), NFC by `string.Normalize()` first. 50,280 vocab,
  50,009 merges, specials `[CLS]`=50281 `[SEP]`=50282 `[PAD]`=50283 `[MASK]`=50284.
  Where the package disagrees with Hugging Face's on a fixture, the fallback is a
  hand-written GPT-2 BPE of about 150 lines, the ADR-0019 way.
- `Sequence`: the port of Laya's `build_sequence` and `render_options`: `[CLS] <type>
  question: <instructions> [SEP] [MASK] opt0 [MASK] opt1 … [SEP] state [SEP]`, `max_len`
  512, `head_max_len` 192, 48 tokens per option, the budget rule at `common.py:102-107`,
  left truncation for list states.
- `Calibration`: a temperature per `temperature_by_options` bucket, clamped to 0.5–5.0,
  and confidence from entropy. The English checkpoint ships an invalid temperature for a
  choice of 11 or more options, so those confidences are uncalibrated.
- `LayaSession`: the ORT `InferenceSession`, collation and softmax.
- Packages: `Microsoft.ML.OnnxRuntime` 1.30 and `Microsoft.ML.Tokenizers` 2.0, pinned in
  `Directory.Packages.props`, lock files regenerated with `dotnet restore Flyback.slnx
  --force-evaluate`. Check them first (the `nuget-packages` skill).
- Per platform: `Directory.Build.targets` gets a `Native="true"` metadata on
  `PluginProject` that passes `RuntimeIdentifier=$(RuntimeIdentifier)` to the plugin's
  two MSBuild calls, and the csproj sets `RuntimeIdentifiers` for win-x64, win-arm64,
  osx-arm64, osx-x64 and linux-x64 with `AppendRuntimeIdentifierToOutputPath=false`.
  ADR-0028 reserved this revisit for this plugin alone. `PluginLoadContext.LoadUnmanagedDll`
  already finds natives through the plugin's `.deps.json`.
- Listed as a `PluginProject` in `Flyback.Editor.Desktop.csproj`, `Flyback.Plugins.Tests`
  and `Flyback.Specs`, and in `Flyback.slnx`. Once it names `IPreparedModel` and
  `ModelFile`, both come off `ContractSurfaceTests.Promised`.

### Tests

`tests/Flyback.Plugins.Laya.Tests`: the tokenizer and `Sequence` against the export's
fixtures, `Calibration` against Python's numbers, and `LayaSession` within 0.02 of
Python's answers behind `Assert.SkipWhen(!File.Exists(model), "no Laya model on this
machine")`. `Plugins.Tests`: it loads, `Unavailable` says "not downloaded" with no model
on disk and no session built. By hand once the export is up: `flyback-cli decide
--prepare`, then `flyback-cli decide "we were billed twice" --yes-no "Is this about
money?" --json`, and the same through a `laya-serve` on the Jev plugin, to compare.

### Docs, in the same commit

`docs/plugin-guide.md` (the plugin in the shipped list), `docs/engineering-guide.md`'s
table of what ships, `site/index.html`, the README's build note for the export script, a
`CHANGELOG.md` bullet, and an amendment to ADR-0186 for the native build.
