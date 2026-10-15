# ADR-0198: A release ships the Android editor as an APK signed with a keystore of its own

**Status:** Accepted · 2026-10-15 · *user-directed* · builds on
[0184](0184-the-editor-runs-on-android-with-its-plugins-linked-in.md) and
[0088](0088-a-release-installs-itself-at-the-next-start.md)

## Context

The Android editor was built on every push but shipped nowhere: CI's APK is signed with
the debug key and kept for two weeks.

Neither open Android store takes it. F-Droid builds every app from source on Debian,
where .NET cannot be bootstrapped and a distribution's SDK takes no workloads. IzzyOnDroid
lists APKs from a project's releases, but its
[inclusion policy](https://izzyondroid.org/docs/general/AppInclusionPolicy/) rejects
apps whose code is written in part by generative AI, and Flyback's is.

## Decision

**Each GitHub release carries `flyback-<version>-android-arm64.apk`**, which a person
installs by hand or follows with an updater that reads GitHub releases.

**It is signed with a keystore of its own**, the repository secrets `ANDROID_KEYSTORE`
(a PKCS12 file in base64, the key under the alias `flyback`) and
`ANDROID_KEYSTORE_PASSWORD`. Not `RELEASE_SIGNING_KEY`: Android signs with an RSA key in a
keystore, and that key signs `SHA256SUMS`.

**The version code rises with the version**: major × 1,000,000 + minor × 1,000 + patch,
worked out by `scripts/release-version.sh`, which `release.sh` sources for the version
too.

**arm64 alone.** x64 is for the emulator, and phones and tablets are arm64.

**It builds beside `release.sh`, not in it.** The android workload is not in the gate
image (0184), so the Release workflow's `android` job builds it on GitHub's runner,
checking the keystore first. A `publish` job tags and publishes only once both have
passed. `release.sh` run here builds no APK.

## Consequences

- **The keystore is for good.** Android installs an update only over an APK signed with
  the same key, so a lost keystore means every user uninstalls and starts again. It is
  kept somewhere besides the repository secret.
- A Release run without the two secrets fails in the `android` job, before anything is
  published.
- The Flyback updater (0088) does not touch Android; the APK is not in `SHA256SUMS`, and
  its own signature is what Android checks.
- If IzzyOnDroid's policy changes, the release already carries what it needs except the
  Fastlane metadata.
