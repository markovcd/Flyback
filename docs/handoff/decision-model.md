# A decision model behind the plugin boundary

Planned on 2026-09-28. It is on TODO.md; take it off there, and delete this file, in the
commit that lands it.

## Why

Jev (TypeSafe AI, hosted) and Laya (Convai Innovations, Apache-2.0 open weights) are
"System One" models: one forward pass over a state (text or JSON) answers typed
questions with calibrated probabilities. Three question kinds: `choice` (one of N
labeled options), `score` (a level on an ordered rubric), `noul` (probability that a
statement holds). No text generation, so nothing to parse and nothing to hallucinate,
and a number that says how sure it is. On a CPU Laya answers in 200–460 ms; on a T4
in 33 ms.

Both speak one wire protocol, `POST /v1/systemone`:

```json
{"state": "...", "model": "jev-latest",
 "questions": {"urgent": {"type": "noul", "instructions": "Does this message express urgency?"},
               "dept":   {"type": "choice", "instructions": "Which department?", "criteria": {"billing": "...", "other": "..."}},
               "level":  {"type": "score", "instructions": "How loud?", "criteria": ["quiet", "medium", "loud"]}}}
```

```json
{"model": "jev-1.13.0",
 "answers": {"urgent": {"type": "noul", "noul": 0.97},
             "dept":   {"type": "choice", "choice": "billing", "probabilities": {"billing": 0.9, "other": 0.1}, "confidence": 0.8},
             "level":  {"type": "score", "score": 1.7, "legend": {"0": "quiet", "1": "medium", "2": "loud"}, "probabilities": {"0": 0.1, "1": 0.2, "2": 0.7}, "confidence": 0.6}},
 "usage": {"input_tokens": 392, "output_tokens": 0}}
```

Flyback has no classifier, no confidence value anywhere, no ML package and no
first-use download. Every decision below is a substring match, a count, a constant,
catalog order, or the chat assistant. Plugins can add sound backends, modules,
presets, assistants, secret stores and MIDI inputs, nothing else.

**Decided:** the local runtime is ONNX in-process (no Python); the model file is
hosted in a Hugging Face repo the maintainer owns; hosted Jev ships as an HTTP plugin;
the first features to call it are assistant routing and gating, finding a module by a
phrase, and diagnostics triage.

## Nothing installed by hand

The plugin ships ONNX Runtime (native, per platform, ~15–30 MB, inside the plugin
folder the way NAudio ships in WinIO; `PluginLoadContext.LoadUnmanagedDll` already
resolves natives through the plugin's `.deps.json`) and a byte-level BPE tokenizer in
managed code (`Microsoft.ML.Tokenizers` 2.0: `BpeTokenizer.Create(BpeOptions)` with
`ByteLevel = true`, `PreTokenizer = RobertaPreTokenizer.Instance`, which is the GPT-2
regex the checkpoint's `ByteLevel{use_regex: true}` pre-tokenizer is; NFC by
`string.Normalize()` before the call. The checkpoint's tokenizer: 50,280 vocab, 50,009
merges, specials `[CLS]`=50281 `[SEP]`=50282 `[PAD]`=50283 `[MASK]`=50284). The model
itself is downloaded once, with consent, from a repo the maintainer owns: Laya's
`scripts/export_onnx.py --quantize` (opset 18; inputs `input_ids`, `attention_mask`,
`marker_pos`, `marker_mask`, `qtype`; outputs `logits`, `act_logits`; per-channel INT8)
gives a ~450 MB file. Nobody publishes that export, so the maintainer exports it once
(a venv with laya 0.3.20, torch CPU and the English checkpoint exists on the
development machine under `laya-test/`, ignored) and uploads it. Apache-2.0 allows the
redistribution; the model card credits Laya.

What could not be avoided: the export needs Python once, on the maintainer's machine,
never the user's. The multilingual checkpoint (mmBERT, 8k context) is a second export
and download, out of the first cut. CPU only in the first cut (DirectML/CUDA are
follow-ups behind the same plugin). If the package's BPE disagrees with Hugging Face's
on some input, the fixture test says where, and the fallback is a hand-written GPT-2
BPE (~150 lines, the ADR-0019 way).

## The shape

Reusable in Flyback; model-specific in a plugin. Glossary words: **decision model**
(not classifier, System One, oracle, judge), **question** of three kinds **choice**,
**score**, **yes-no** (the wire says `noul`; code says `YesNo`), **answer**,
**confidence**. The settings window gets a **section** named Decisions.

### Flyback.Plugins: the contract (host-owned, every public line is a PublicAPI line)

New namespace `Flyback.Plugins.Decide`, one type per file:

- `IDecisionModel`: `Id`, `Name`, `Priority`, `AssistantCredential? Credential` (null
  for Laya), `Form(SettingValues)`, `string? Unavailable(DecisionConfig)` (no network,
  no throw; "not downloaded yet" is an answer; may only `File.Exists`),
  `Task<Decision> DecideAsync(DecisionRequest, DecisionConfig, CancellationToken)`.
  Registering it costs nothing; the ONNX session is built on the first `DecideAsync`,
  never at `Register` (`PluginHost.Load` runs on every CLI command and in every test
  with no model on disk).
- `IPreparedModel`: the optional second interface, the way `IModelSurvey` is optional
  beside `IPatchAssistant`. `IReadOnlyList<ModelFile> Needs` (each `ModelFile(Uri,
  Name, Sha256, Size)`) and `bool Prepared(string folder)`. The plugin declares; **the
  host downloads and verifies**, so the plugin never touches the network and the
  install dialog's "reaches the network" stays false for Laya.
- `DecisionConfig(IAssistantTransport Transport, SettingValues Values, string? Folder)`:
  `Folder` is where the host put the model's files. A transport rather than a key, as
  an assistant gets ([0158](../adr/0158-a-plugin-loads-only-once-somebody-said-yes-and-never-holds-a-key.md)).
- `DecisionRequest(string State, IReadOnlyDictionary<string, Question> Questions)`;
  `Question` abstract record: `Choice(Instructions, options label→description)`,
  `Score(Instructions, levels)`, `YesNo(Instructions)`. Limits as laya-serve's: 64
  questions, 50k chars.
- `Decision(string Model, IReadOnlyDictionary<string, Answer> Answers, DecisionUsage
  Usage)` (not `Usage`: `Flyback.App.Statistics.Usage` exists); `Answer.Chosen(Option,
  Probabilities, Confidence)`, `Answer.Scored(Score, Probabilities, Confidence)`,
  `Answer.YesNo(Probability)`.
- `SystemOneWire`: hand-written System.Text.Json read and write of the protocol, as
  `Flyback.Plugins.OpenAi/Wire.cs` does chat-completions. Used by the HTTP plugin,
  `flyback-cli decide --json`, and the Laya tests (its answers serialize to the same
  shape).
- `DecisionSettings` (internal): copy of `AssistantSettings` with `Model` and `File =>
  <data folder>/decisions.json`.
- `ModelStore` (internal, in Flyback.Plugins so both the editor and `flyback-cli` reach
  it through the existing `InternalsVisibleTo`): `FetchAsync(ModelFile, string folder,
  IProgress<Fetching>, ct)` into `<data folder>/models/<plugin id>/`, `.partial` then
  rename, hash through a `CryptoStream` as `UpdateDownloader.DownloadAsync` does
  (`src/Flyback.App/Updates/UpdateDownloader.cs:128-153`), capped by `Size`, refused on
  a hash mismatch. The SHA-256 is pinned in the plugin next to the Hugging Face
  revision, so a file changed upstream is refused rather than run. No release
  signature: the pinned hash is the signature.
- `IPluginRegistry.AddDecisionModel(IDecisionModel)`: the seventh method, same "must
  not" remark as `AddPatchAssistant`. `PluginHost.Registry` gets the list, the
  checkpoint count and the duplicate-id `PluginProblem` (`PluginHost.cs:271-274,
  296-313, 381-393`, `Catalog(...)` `:87-97`); `PluginCatalog` gets a trailing optional
  ctor parameter (positional callers at `AssistantSelectionTests.cs:15` and
  `ProbeCommandTests.cs:359`), `DecisionModels`, `PreferredDecisionModel`,
  `DecisionModel(id)`.
- `PluginContractVersion` stays `1.0.0` in this commit: Core already carries 43
  unshipped lines at that number and `/release` recomputes the version from the
  surface. The ADR says so in a line.

### The host (Flyback.App; the CLI shares the internal pieces)

- `src/Flyback.App/Decide/`: `DecisionSettingRepository` and `ChosenDecisionModel`
  (mirrors of `AssistantSettingRepository`, `ChosenAssistant`); `Decisions`, the one
  entry point a feature calls: the chosen model, its config (key through the existing
  `Credentials` with `model.Id` and the credential's variable, as
  `AssistantPanel.Configured()` does at `:880-887`), a per-call timeout, and
  `Decisions.None`, the null object every feature holds when nothing is installed,
  prepared or consented: it answers nothing and the feature keeps today's behavior.
  Helpers written once: `Gate(answer, threshold)` and `Rank(items, ≤10 per question,
  with a "none of these" option)`.
- `DecisionsSection` in the settings window after Assistant (`SettingsSession.cs` ctor
  `:12-23`, tabs `:43-53`, save `:58-64`, show `:70-76`; section shape as
  `UpdatesSection.cs`): the model picker and its `SettingsForm` (as
  `AssistantPanel.BuildSettings` `:612-642`), a status line (prepared or not, size,
  folder), a Download button with progress posted to the UI thread (as
  `ProbeSection.cs:126-260`), a Test box running one yes-no question. `DownloadAnswer`
  enum, Cancel first, for the consent dialog through `IDialog`.
- Consent, once: the first feature that needs a model that is not prepared shows one
  dialog ("Laya, 450 MB, from huggingface.co/<owner>/laya-onnx. Download?"); declined
  means `Decisions.None` for the session and a line in the section. Never silent
  (ADR-0132's spirit).
- Registration in `EditorServices.AddEditor` beside `:104-110`; a named `HttpClient`
  for downloads next to `SiteAccess.Client` at `:80`; `EditorSetup.DecisionSettingsPath`
  and `ThisMachine` (`:54, :118-124`).
- `flyback-cli decide` (`Commands/DecideCommand.cs`, `Models/DecideOptions.cs`, factory
  in `Program.cs:76-89` on the `Probe` template `:198-263`): `decide [<state>|-] --ask
  <questions.json> [--model <id>] --json` prints the protocol's answer object through
  `SystemOneWire`; one-question shorthands `--yes-no "..."`, `--choice "..." --option
  a=... --option b=...`, `--score "..." --level ...`; `decide --status [--json]` lists
  models, prepared or not, sizes; `decide --prepare [<id>] [--yes]` downloads after an
  `Agreed` question copied from `ProbeCommand.cs:121-160`. State on stdin means there
  is no reader for a question, so `--prepare` then needs `--yes`; `--prepare` is its
  own invocation anyway. Exit codes from `Exit`.

### Plugins

- `src/Flyback.Plugins.Laya` (in the box): `LayaPlugin`, `LayaModel : IDecisionModel,
  IPreparedModel`, `LayaFiles` (pinned revision; `laya-int8.onnx`, `tokenizer.json`,
  `rl_agent_config.json` with sizes and SHA-256s), `LayaTokenizer`, `Sequence` (the
  port of Laya's `build_sequence`/`render_options`: `[CLS] <type> question:
  <instructions> [SEP] [MASK] opt0 [MASK] opt1 … [SEP] state [SEP]`, `max_len` 512,
  `head_max_len` 192, 48 tokens per option, the budget rule at `common.py:102-107`,
  left-truncation for list states), `Calibration` (temperature per
  `temperature_by_options` bucket, clamped to 0.5–5.0, entropy confidence),
  `LayaSession` (ORT `InferenceSession`, collate, softmax). Packages
  `Microsoft.ML.OnnxRuntime` 1.30 and `Microsoft.ML.Tokenizers` 2.0, pinned in
  `Directory.Packages.props`, lock files regenerated (`dotnet restore Flyback.slnx
  --force-evaluate`; the gate restores locked). Per platform: `Directory.Build.targets`
  gets a `Native="true"` metadata on `PluginProject` that passes
  `RuntimeIdentifier=$(RuntimeIdentifier)` to the two MSBuild calls (`:107-135`), and
  the csproj sets `RuntimeIdentifiers` for win-x64, win-arm64, osx-arm64, osx-x64,
  linux-x64 with `AppendRuntimeIdentifierToOutputPath=false`. ADR-0028 reserved
  exactly this revisit "for that plugin alone". `release.sh` and the Dockerfile need
  nothing.
- `src/Flyback.Plugins.SystemOne` (in the box, portable): `SystemOnePlugin`,
  `SystemOneModel` with a `Pick` for the endpoint (`https://api.typesafe.ai`, `Editable:
  true` so a LAN `laya-serve` works), a `Text` for the model id (`jev-latest`),
  `AssistantCredential("TYPESAFE_API_KEY", "Get one at console.typesafe.ai/keys")`,
  `HttpClient` with an injectable handler, bearer only when a key is set,
  401/422/429/529 as sentences. Priority below Laya, so Laya is the default when both
  are there. csproj copied from `Flyback.Plugins.OpenAi.csproj`, "No package" comment
  and all.
- Both as `PluginProject` lines in `Flyback.App.csproj:74-87`, `Flyback.slnx:37-51`, and
  in `tests/Flyback.Plugins.Tests` and `tests/Flyback.Specs` csproj plugin lists.
- `tests/Flyback.Plugins.FakeDecider`: `ScriptedDeciderPlugin`, `ScriptedDecider`
  (priority -100, answers from a script keyed by question id, records what it was
  asked), one type per file, csproj from `FakeAssistant`; a `ProjectReference` in
  `App.Tests` (as `FakeAssistant` at `:43`) and a `PluginProject` in `Plugins.Tests` and
  `Specs`.

### Once, by the maintainer: the export

`scripts/laya/export-onnx.ps1` runs the venv's Python on Laya's
`scripts/export_onnx.py --quantize`, writes the test fixtures (below), prints SHA-256s
and sizes, and says what to upload (`laya-int8.onnx`, `tokenizer.json`,
`rl_agent_config.json`, a README crediting `convaiinnovations/laya`). The maintainer
creates the Hugging Face repo and uploads; the revision hash and the hashes go into
`LayaFiles`. The one Python step, documented as such in the README's build section.

## The first three uses

Each degrades to today's behavior on `Decisions.None`; each has a scenario on the
fake model.

1. **The assistant, routed and gated** (`AssistantPanel.cs:1224-1258, 1380-1401`,
   `AssistantRun`).
   - Before `conversation.Ask(wanted)`: one `choice`, *what is being asked* — edit this
     patch / start a new patch / a question about this patch / a question about a
     module / load a preset / not about Flyback — and a `yes-no`, *does answering need
     to see or hear it*. The transcript shows a `Voice.Note` "Read as: a question about
     the patch" and `log.Write("intent", …)`; the raw text stays as `Voice.You`. A
     question turns render and listen off for that turn and prefixes the instruction
     with one line ("Intent: a question about the patch. Answer; do not propose.") — a
     prefix on the instruction, which both sessions append after the byte-stable
     briefing (`OpenAiSession.cs:138`, `GeminiSession.cs:117`), so no contract change
     and no cache loss. "Not about Flyback" above 0.9 gets a one-line reply without a
     run; below it runs as usual. One sentence in `Handbook.Conventions` says the
     editor may prefix an `Intent:` line. State: the instruction, a printed summary of
     the patch, the last few transcript lines.
   - After the loop, when the run edited and `run.Proposal is null` (host-side, from
     the workbench the host owns; no `PatchEvent` change): a `yes-no` over the
     concatenated `Said` text, *does the reply say it is finished*; yes above the gate
     sends one follow-up `Ask("propose")` so the edits are offered rather than lost.
   - Before `Deliver`: a `yes-no`, *does the proposal's summary do what was asked*, run
     in `AskAsync` after the loop and passed into `Deliver`; below the gate the
     proposal is still applied (undo exists) and a `Voice.Aside` notice says so with
     the number. ADR-0047's warning holds: the state is the instruction and the printed
     patch, never the assistant's own summary alone.
2. **A module by a phrase** (`ModulePalette.cs:342-436, 641-658`,
   `CatalogReference.cs:54-89`, `ModulesCommand.cs:49, 161-173`). A headless
   `ModuleFinder` service built by the container (`Palette.cs:82`): substring first;
   when nothing matches, or the phrase has more than two words, one `choice` over the
   15 categories (ordering is valid, its confidence is not gated on: Laya's
   `choice:11+` temperature is uncalibrated), then a `choice` over the top two
   categories' modules chunked to 10 with a "none of these" option, ranked by
   probability. The palette keeps `Highlight(0)` so Enter adds the top one; the query
   runs asynchronously and is cancelled by the next keystroke. `find_modules`, `Nearest`
   and `flyback-cli modules --find "<phrase>"` return the same ranking with
   probabilities. State: the phrase plus each candidate's name and description. 92
   built-in modules, 131 with the in-box plugins.
3. **Diagnostics triage** (`PatchCompiler.cs:86-107, 141-158, 370-378`,
   `CheckCommand.cs:37-47`, `Playback.cs:269-303`, `ClipLevels.cs`). `IssueTriage`
   (host-owned): with more than one issue, one `score` per issue, *how likely this is
   why the patch is silent or dark*; likeliest first in `check --json` (a `likely`
   field on each complaint; `CheckCommand.Run` becomes `Task<int>` as `ProbeCommand.Run`
   is), in the workbench's `check` tool, and in the editor's status line by a second
   `report.Say` when the answer arrives, only if the patch has not changed since. A
   measured clip gets one `choice` word for the assistant beside the numbers:
   continuous / rhythmic / percussive / silent.

## Where else it pays (proposals, not in this cut)

In the order to take them: keeping the conversation when the patch changes underneath
(TODO already asks; a `yes-no` on the diff instead of the node-count heuristic at
`AssistantRun.cs:176`); which socket a wire dropped on a module's body meant
(`WireDrop.SocketOn`, kind-then-first today); is a range mismatch an intended
modulation (`AutoRemap.Offered`); the preset site holding an upload for review
(`yes-no` on name and description; plugins already arrive unpublished, presets go live
at once at `Program.cs:223`) and suggesting tags from the site's top 60; a letter's
mood and urgency pre-filled; which instrument profile a MIDI port is
(`InstrumentProfile.Matches`); the briefing budget ADR-0098 calls a guess, scored per
request. Every one is a question over text that already exists, which is why the
contract is worth more than any single use.

## Constraints and collisions

- ADR-0019 and ADR-0033: no package in Core or Engine; runtime and network client are
  plugins; the contract types live in Flyback.Plugins as the assistant's do. ADR-0034:
  the key through `AssistantCredential` and `Credentials`; a nullable `Credential` is
  the one deliberate difference from `IPatchAssistant`.
- ADR-0028: the per-platform plugin build is the revisit it reserved; the ADR names
  Laya as that plugin.
- ADR-0069: `SettingField` has three shapes; status and the Download button are drawn
  by the host from `IPreparedModel`, as `ProbeSection` is drawn for `IModelSurvey`.
- ADR-0144, `Palette.cs:150-156` and ADR-0045 refuse to guess on purpose; none of the
  three uses guesses there.
- ADR-0150 and the drivability rule: `ModuleFinder`, `IssueTriage`, `Decisions` and the
  section are services the container builds; every path has a CLI form.
- `one-type-per-file.md`: every new class its own file; `RehearsedAssistantPlugin.cs`
  (three types) is left alone unless touched.
- `nuget-packages` skill first: check the SDK and packages before touching many files.
  `changelog`, `website`, `tests`, `adrs`, `build-artifacts` skills at the end.

## Implementation sequence

A. Contract: `src/Flyback.Plugins/Decide/*` (the types above), `IPluginRegistry.cs`,
   `Hosting/PluginHost.cs`, `Hosting/PluginCatalog.cs`, `PublicAPI.Unshipped.txt`,
   `Flyback.Plugins.csproj` `InternalsVisibleTo` for the two new test projects.
B. Host: `src/Flyback.App/Decide/*` (`ModelStore` lives in Plugins, the rest here),
   `EditorSetup.cs`, `EditorServices.cs`, `Settings/SettingsSession.cs`.
C. Uses: `Assist/AssistantPanel.cs` (+ one sentence in `Handbook.cs`),
   `Canvas/ModuleFinder.cs` + `ModulePalette.cs` + `Palette.cs` + `CatalogReference.cs`
   + `ModulesCommand.cs`, `IssueTriage` + `CheckCommand.cs` + `Program.Check` +
   `Playback.cs`.
D. CLI: `Commands/DecideCommand.cs`, `Models/DecideOptions.cs`, `Program.cs`.
E. Plugins: `src/Flyback.Plugins.SystemOne/*`, `src/Flyback.Plugins.Laya/*`,
   `Flyback.App.csproj`, `Directory.Packages.props`, `Directory.Build.targets`
   (`Native` metadata), `Flyback.slnx`, lock files.
F. Tests: `tests/Flyback.Plugins.FakeDecider/*`; `tests/Flyback.Plugins.Laya.Tests`
   (tokenizer, sequence, calibration against fixtures; model tests behind
   `Assert.SkipWhen(!File.Exists(model), "no Laya model on this machine")`);
   `tests/Flyback.Plugins.SystemOne.Tests` (the `Canned` handler pattern from
   `OpenAi.Tests/SessionTests.cs:700-720`: request shape, 200/401/422/429);
   `Plugins.Tests`: `DecisionPluginTests` (identity across the boundary, duplicate id),
   `DecisionModelSelectionTests`; `Cli.Tests/DecideCommandTests` (`StringReader` for
   the question); `App.Tests`: `DecisionsSectionTests`, `ModuleFinderTests`,
   `IssueTriageTests`, `AssistantRunTests` additions; `Specs/Features/Decisions.feature`
   + `Steps/DecisionSteps.cs` (CLI through `PluginHost.Load` as `PresetSteps.cs:16`,
   not `PluginCatalog.Empty`).
G. Docs, same commit: `site/plugins.html:270-289` ("six methods" → seven, a table row,
   the plugin list), `site/index.html:439-447, 589-599`, `site/tutorials.html:530,
   580-620`; `docs/engineering-guide.md:333-341, 384-393, 700-711`;
   `docs/glossary.md:112-115, 138-149`; `CHANGELOG.md` one bullet under Unreleased; a
   new ADR (numbered from `main` at commit time) + `docs/adr/README.md` row; README
   build note for `scripts/laya/export-onnx.ps1`.
H. Land: commit, fast-forward `main`, then `release.sh` (build-artifacts skill).

## Verification

- Unit: `SystemOneWire` round-trips the two examples above; `ModelStore` refuses a
  wrong hash and leaves only a `.partial`; `LayaTokenizer` and `Sequence` against
  fixtures the export script writes from Python (token ids and marker positions for
  ~20 states × questions, checked in); `LayaSession` against Python's answers for the
  same fixtures within 0.02 probability, skipped without the model file; the HTTP
  plugin on the canned handler.
- Plugins.Tests: both plugins and the fake load; a second model with the same id is a
  `PluginProblem`; `Unavailable` says "not downloaded" with no model on disk and no
  session built.
- Specs on the fake: "A question about the patch is answered, not proposed"; "Edits
  the assistant forgot to offer are offered"; "A phrase finds the module it describes";
  "The likeliest cause is the first complaint"; "Without a decision model everything
  works as before"; "`decide` answers in the protocol's JSON"; "A model is downloaded
  only after a yes".
- By hand once the export is up: `flyback-cli decide --prepare`, then
  `flyback-cli decide "we were billed twice" --yes-no "Is this about money?" --json`,
  and the same through `laya-serve` on the HTTP plugin to compare answers and `usage`;
  the settings section's Test box in the real window (running-the-app skill).
- Suites once before the commit; durations ranked (tests skill).
