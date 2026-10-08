# The pipeline

## One gate, the same everywhere

`docker build --target gate .` is the gate, and CI runs exactly that through `scripts/gate.sh` (`ci.yml`), not a list of steps that resembles it (ADR-0120). The image carries the fonts the headless UI tests rasterize with and the ffmpeg the recording tests look for, so nothing skips in CI that runs locally. The stages stack: `publish` builds on `gate`, so no artifact exists unless every test passed. Restores are locked to the committed `packages.lock.json` files, so a version that moved fails the gate instead of building.

**Why:** two descriptions of what a change has to pass drift apart, and the one that drifts is the one nobody runs locally.

## Workflows are code that holds the keys

- **Every action is pinned to a commit SHA**, with its version in a comment. A tag is the action author's to move. `.github/dependabot.yml` says when a pin should move.
- **Least permission.** Each workflow declares `permissions:`, `contents: read` unless it publishes.
- **Secrets only where needed.** `RELEASE_SIGNING_KEY` reaches the Release and Site workflows and nothing triggered by a pull request.
- **The self-hosted runners are for `main` only**, never a pull request or another branch: the repo is public, so a fork's PR on that machine would be remote code execution. `ci.yml` waits for one, Windows or Linux, and either needs Docker with buildx; GitHub fails a job nothing picks up after 24 hours. The runner's location is machine-specific and lives in the assistant's memory, not here.
- **One builder, a capped cache.** A self-hosted machine builds on the single `flyback` buildx builder that `scripts/builder.sh` creates, its layer cache held under 15 GB by `scripts/buildkitd.toml`, so a cancelled job leaves nothing behind and disk use is bounded. The builder's container is held to 12 GB of memory (`FLYBACK_BUILDER_MEMORY`), so a heavy build dies inside it instead of freezing the host. GitHub's own runners use a fresh builder and GitHub's cache.
- **Superseded runs are cancelled.** `concurrency` with `cancel-in-progress` on anything a push triggers; never on Pages or a release, which must finish what they started.
- **Every job sets `timeout-minutes`**, a few times its usual length. The default is six hours of a hang.
- **Deploys filter on paths.** `worker.yml` lists what the Worker, its pages and the plugins it starts with are built from; a new reference or site plugin is added there in the same commit.
- **Every workflow opens with a comment** saying what it does, what triggers it, and why anything surprising in it is there.

## A release is one script

`release.sh` is the release: the gate, the publishes, the pack and the signing. The Release workflow runs it and adds only the publishing. Run here, it signs with the local test key (the `release-key` skill) and publishes nowhere, so trying a release is `./scripts/release.sh`, never a dispatch (the `build-artifacts` skill). It holds `release.lock` while it runs, and a second build refuses rather than emptying `dist/` under the first.

## A release refuses before moving anything

`/release` does the checks, and stops on the first that fails: a tree that is not clean, a `main` that is not what would be released, a tag that exists, an empty `## Unreleased`, a locked restore that fails, and a gate that has not passed on that exact commit. **No CI run at all is not a pass.** Everything up to the dispatch is safe to run any time; the dispatch waits for the user's go.

## Versions come from tags

The version is the next minor after the latest `vX.Y.Z` tag, or the one given, passed as `-p:Version=`; nothing edits it into a file. Every other build is `-dev` with the commit, so it never updates itself and is never counted as a release. A Release run is named for its version, and its GitHub page is that version's CHANGELOG.md section.

## What ships is signed

A release carries a signed `SHA256SUMS`, and Flyback installs an update only if the signature verifies against the committed `release-key.pem` (ADR-0088). `release.sh` checks the key pairs with that public key first, so a bad key fails in seconds rather than after a release nothing can install. The preset site's plugins are signed with the same key.

## Measure on a schedule, gate on correctness

Coverage runs on Mondays (`coverage.yml`), outside the gate, with no threshold: it is for noticing that something stopped being covered. Benchmarks are run by hand. The gate fails only on what is wrong.

## Read the whole log

Capture a build's output to a file and filter it afterwards, never in the pipe; a narrow grep drops the `failed <name>` lines. A gate failure is the first sentence of the report, with the failing test's name.
