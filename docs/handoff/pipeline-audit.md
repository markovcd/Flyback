# Where the pipeline is weakest, and what would fix each point

Written on 2026-10-10, on `main` at `5523912`. An audit, not a plan: each item is
a proposal, and goes to TODO.md only once the user picks it. Delete an item here
in the commit that lands it, and the file when the last goes.

The run history was read from the Actions API: the last 40 Build runs on `main`
(594 to 633), the last 20 Android and Worker runs, the last 10 Validate runs, and
the jobs of the runs named below. Everything else was confirmed by reading the
workflows, the scripts and the Dockerfile; nothing was built or run here.

What holds and is not raised again: one gate, built the same by CI, `release.sh`
and `coverage.sh` (ADR-0120); every action pinned to a commit, every workflow
with `permissions:`, every job with a timeout, and `WorkflowTests` failing on a
workflow that is not; a verdict per commit on `main`, never cancelled; a hang
dump that names the test a hung host was in; pull requests off the self-hosted
machine; the signing key only ever a build secret, paired with the committed
public key before anything is built; the restore locked; a stranger's patch
rendered with no token in the step. A Build run on `main` takes about four and a
half minutes when the layer cache is warm, and nothing in the last forty runs
timed out.

## 3. The Windows-bash dance is written three times (Low)

`ci.yml` and `coverage.yml` each carry a Linux step and a Windows step running
`"C:/Program Files/Git/bin/bash.exe"`, and `release.yml` the same pair spelled
`C:\PROGRA~1\Git\bin\bash.exe` through `shell:`. One feature, three copies,
two spellings. GitHub's documentation for `shell: bash` says that on a Windows
runner it is the bash included with Git for Windows, which would collapse each
pair into one step with no path in it. Not verified on the self-hosted Windows
runner, whose PATH is what put WSL's `bash` first.

**Fix:** try `shell: bash` once on the Windows runner. If it holds, six steps
become three. If it does not, a composite action under `.github/actions/gate`
holds the pair once and the three workflows call it.

## 4. Nothing pins the SDK or a base image (Medium)

`global.json` names a test runner and no SDK version. The gate builds on
`mcr.microsoft.com/dotnet/sdk:10.0`, Android on `setup-dotnet` at `10.0.x`,
Validate on `sdk:10.0` again, Worker on `runtime:10.0` and `node:24-bookworm-slim`,
the Dockerfile's `worker` stage on `node:24-bookworm-slim` a second time. The
restore is locked, so no package moves, but the compiler, the wasm-tools
workload and the Android workload move with each SDK patch, silently. A release
built tomorrow is not the gate that passed today, which `ci-cd.md` says it is.
`dependabot.yml` says the base images are not listed because an `ARG` is not a
literal `FROM`; so nothing says when they moved.

**Fix:** pin, the same way the actions are pinned. A `base` stage holds the one
literal `FROM mcr.microsoft.com/dotnet/sdk:10.0.<patch>@sha256:…` with its apt
packages (item 5), and `dependabot.yml` gets a `docker` entry that reads it.
`global.json` names the same SDK with `rollForward: latestPatch`, so a developer's
build and the gate agree. `worker.yml` and `validate.yml` take their image tags
from the Dockerfile's stages, or carry the same digest with a comment saying
where it is kept. The alternative is a line in `pipeline.md` saying base images
float on purpose; either is a decision, and neither is made today.

## 5. Three apt lists for one set of packages (Low)

`built`, `site-build` and `renderer` each start from the SDK image and each run
their own `apt-get install`: the renderer's list is a subset of the gate's, and
`site-build` installs ffmpeg, python3 and the wasm-tools workload the gate
already has. Three lists drift one package at a time, and the workload install
runs twice on the Worker deploy.

**Fix:** `FROM ${SDK} AS base` with the packages and the workload once, and the
three stages on top of it. One list, one cached layer. A refactor of its own,
with nothing else in the commit.

## 6. Validate's net is coarser than its header says (Low)

The schedule is `*/10 * * * *`, and the header calls it the net for a dispatch
the Worker lost. GitHub fired it five times on 2026-10-09: 06:25, 13:33, 18:57,
23:02, and 02:16 the next morning; five to six hours apart, not ten minutes.
GitHub drops frequent schedules under load, and says so in its documentation. A
lost dispatch waits hours.

**Fix:** either the header says hours, or the Worker retries a dispatch it does
not see picked up within a minute, which is the Worker's own `wrangler` cron.
The schedule stays as the last net.

## 8. Small

- Pull-request runs export every layer of every stage to the Actions cache with
  `--cache-to type=gha,mode=max` on each push. The repository's cache is 10 GB
  and the image is several, so each Dependabot push evicts the last.
  `mode=min` keeps the layers the final stage reaches, which is what the next
  run reads.
