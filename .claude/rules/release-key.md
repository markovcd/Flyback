# The release key on this machine

## One variable, and a missing one is made

Everything that signs reads `RELEASE_SIGNING_KEY`, the variable the Release workflow reads its secret into: `release.sh`, `deploy/presets/deploy.sh`, the preset site's image build and a Release run of the site. On this machine it holds a local test key, a P-256 PEM kept in the user environment. A Debug build checks no keys at any stage.

If it is missing, make a new one without asking: `. ./release-key.sh` does it, and so do a build of the app and a Release run of the site. The app's public key is never edited by hand: a local build derives it from the variable into `obj/`, and `release.sh` and `coverage.sh` hand it to Docker, while the committed `release-key.pem` stays the real one. Never ask for the real release key and never put it in the variable.

**Why:** the user wants local builds to run end to end with no manual step, and said outright to generate a new key every time it is found missing.

**How to apply:** a shell or Rider started before the variable was set does not see it; `release-key.sh` and the site read the user environment directly for that reason. A new key makes the Figures an editor installed from the local site a different plugin, so remove it from the editor and install it again after one is made. To try a release, run `./release.sh` rather than dispatching the workflow: it is the same build, into `dist/`, published nowhere.
