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
