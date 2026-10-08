# A decision model that runs here: the Laya plugin, and what to ask it

Planned on 2026-09-28; the rest of it landed on 2026-10-08 and was measured against a local
laya-serve the same day, on `main` at `71fb09b2`, and again once each use could name its checkpoint. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the Laya plugin.

## Where it stands

Landed ([ADR-0186](../adr/0186-a-decision-model-answers-typed-questions-behind-the-plugin-boundary.md)):
the contract in `src/Flyback.Plugins/Decide` (`IDecisionModel`, `IPreparedModel`,
`ModelFile`, the questions and answers, `SystemOneWire`), `Decisions` as the one door,
`ModelStore` downloading pinned files, `DecisionSettings` in the `decisions` section of
`settings.json`, the Decision server plugin (`src/plugins/Flyback.Plugins.SystemOne`), `flyback-cli
decide`, Settings → Decisions, and the uses: the module list by meaning (`ModuleFinder`,
`modules --find`), complaints in order (`IssueTriage`, `check --triage`, the status line)
and the assistant's messages read before sending (`TurnReading`).

Until somebody chooses, questions go to the likeliest installed model that sends nothing
anywhere. Today none is installed, so nothing is asked until the Decision server is chosen. Laya is the
model that makes the default do something.

Left out of the landed work on purpose:

- The forgotten-proposal follow-up: `TurnLoop.Unproposed` already tells the assistant once.
- The workbench's `find_modules` by meaning: a tool answer is synchronous, and an assistant
  searches well enough by words.
- Senses off for a turn read as a question: the workbench decides senses per conversation,
  not per turn.

## Measured against a local laya-serve

### Running one

On this Linux machine, Laya 0.4.0 is in `~/laya-venv` (Python 3.14, `torch` 2.14.1+cpu installed
first from `https://download.pytorch.org/whl/cpu`, then `pip install "laya[serve]"`), and both
checkpoints are in the Hugging Face cache. Serve it on loopback only; its default is 0.0.0.0:

```bash
LAYA_DEVICE=cpu LAYA_HOST=127.0.0.1 LAYA_PORT=8000 LAYA_MODELS=english,typed-decisions LAYA_PRELOAD=1 ~/laya-venv/bin/laya-serve
```

The Decision server plugin reaches it with its endpoint set to `http://localhost:8000` and no key. A request's
`model` names the checkpoint (`english`, `multilingual`, `typed-decisions`); anything else, the
plugin's default `jev-latest` included, is routed by the text's language, so English text gets
`english`. The answers carry fields `SystemOneWire` does not read (`answer_confidence`, `action`,
`routing`). At startup the English checkpoint warns that its `choice:11+` temperature is invalid,
so a choice of eleven or more options is uncalibrated.

The `flyback-cli` on PATH is the installed release; the one with `decide` is a build of `main`
with `-c "All plugins"`, under `src/Flyback.Cli/bin/All plugins/net10.0/`. The `decisions` section
of `~/.config/Flyback/settings.json` holds
`{"Model":"systemone","Choices":{"systemone":{"endpoint":"http://localhost:8000","model":"jev-latest"}}}`,
written by hand before `decide` could set it (its earlier copy is `settings.json.before-decisions`).
Now it is set from the command line, the module search on its own checkpoint:

```bash
flyback-cli decide --model systemone --set endpoint=http://localhost:8000 --save
flyback-cli decide --for modules --set model=typed-decisions --save
```

To try a setting without touching the real file, point `XDG_CONFIG_HOME` at a scratch folder, or
pass `--set` without `--save` for one `decide` run.

`scripts/decide-bench.py` holds both measurements below: `find <cli>` scores `modules --find`
on fourteen phrases, and `route <endpoint> [checkpoint]` scores TurnReading's rule on eleven
messages.

### What was found

| | English | Typed-decisions |
|---|---|---|
| Module search, category first (what landed first) | 0/14 first | — |
| Module search, every module by name, ten to a question (on `main`) | 1/14 first | 6/14 first, 7/14 in the top three, about 3 s |
| The same in three other orders | — | 3 to 8 of 14 first |
| Two orders in one request, the margin over "none" averaged | — | 8/14 first, 8–9/14 in the top three, about 5.5 s |
| Three orders averaged | — | no better, 9.4 s |
| Catalog order and its reverse, averaged (on `main`, `--for modules` on typed-decisions) | — | 8/14 first, 9/14 in the top three, 6 s warm; the first three asks after a start miss the 10 s deadline |
| Routing: module questions told to answer / others wrongly told (on `main`) | 4/4, 0/7 | 0/4, 0/7 |

- A choice of seventeen categories was close to random, and modules described in full drew
  every answer to the first options shown. Bare names do better.
- Which nine other modules share a question changes the answer: Echo scored 1.00 in one
  grouping and under 0.05 in another, which is what averaging two orders evens out.
- Of the second orders tried with the catalog's, the reverse was kept: each module sits near the
  front once and near the back once, which evens out the pull toward the first options. On fourteen
  phrases the orders scored within two of each other; a pair scoring 10/14 was not chosen on that alone.
- A yes-no per module, on English, scored 4/14 first and 7/14 in the top three at up to 9 s.
- On English, "why is it so quiet?" reads as not about Flyback (0.83) and "what is the capital
  of France?" as a module question, which is why only a module question at 0.8 or above is
  acted on, and nothing is held back. On typed-decisions the module questions read as such at
  only 0.51–0.76, below that bar.

### Next

Landed: each ask names its use and the settings may lay a checkpoint over it, the module search
asks two orders and averages, and `decide --set`, `--for` and `--save` set a model up. Left:

- **The key rows in `Decide/DecisionsSection.cs` and `Assist/AssistantSettingsPage.cs` become
  one control**: both are a key that `Credentials` holds. Proposed, not yet agreed.
- **Settings → Decisions shows a use's settings**: today only the command line writes them, and the
  editor keeps them untouched when it saves.
- **A start that warms the model**: the first asks after laya-serve starts miss the deadline.

Measure each against the server with `scripts/decide-bench.py` before it lands.

## What is left: Laya, in the box

Laya (Convai Innovations, Apache-2.0) answers in one forward pass, 200–460 ms on a CPU. The
plugin ships ONNX Runtime and a tokenizer; the host downloads the model itself, once, after
a yes, from a Hugging Face repo the maintainer owns.

### Once, by the maintainer: the export

`scripts/laya/export-onnx.ps1` runs a venv with laya (0.3.20 there; 0.4.0 is what `~/laya-venv` here has, and the port follows whichever the export used), torch CPU and the English
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
  `Endpoint` null, `Priority` above the Decision server's 10, `Unavailable` only `File.Exists`, and the
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
money?" --json`, and the same through a `laya-serve` on the Decision server plugin, to compare.

### Docs, in the same commit

`docs/plugin-guide.md` (the plugin in the shipped list), `docs/engineering-guide.md`'s
table of what ships, `site/index.html`, the README's build note for the export script, a
`CHANGELOG.md` bullet, and an amendment to ADR-0186 for the native build.
