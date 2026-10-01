# Security policy

## Reporting a vulnerability

Report it privately through [GitHub's security advisories](https://github.com/markovcd/Flyback/security/advisories/new). Do not open a public issue or pull request for it.

Include what you did, what happened and the version you ran. A patch file or bundle that triggers it is the most useful thing to attach.

## Supported versions

The latest release. Fixes land on `main` and ship in the next release.

## What counts

Flyback opens files and loads code that other people write, so these are the places a flaw matters:

- A patch, bundle or sample that crashes, hangs or exhausts memory when opened, or writes outside its folder.
- A plugin that loads without being allowed, or that reaches a stored API key ([ADR-0158](docs/adr/0158-a-plugin-loads-only-once-somebody-said-yes-and-never-holds-a-key.md)).
- An update that installs without its signature verifying ([ADR-0088](docs/adr/0088-a-release-installs-itself-at-the-next-start.md)).
- A flaw in the preset site: injection, an unchecked upload, or one user reaching another's data.
- A secret committed to the repository.

Not a vulnerability: a patch that is slow or loud, or a crash that needs you to run a plugin you chose to allow.
