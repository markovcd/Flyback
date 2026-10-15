# ADR-0198: A release ships the Android editor as an APK signed with the release key

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

**It is signed with `RELEASE_SIGNING_KEY`**, the P-256 key that signs `SHA256SUMS`, so a
release holds one secret. Android checks an update against the signing certificate, not
only the key, so the certificate is made once from the key and committed as
`src/Flyback.Editor.Android/release-cert.pem`. The Release workflow checks that it pairs
with the secret and with `release-key.pem`, joins the two into a keystore for the one
step that signs, and holds the APK to having been signed under that certificate alone.

**The version code rises with the version**: major × 1,000,000 + minor × 1,000 + patch,
worked out by `scripts/release-version.sh`, which `release.sh` sources for the version
too.

**arm64 alone.** x64 is for the emulator, and phones and tablets are arm64.

**It builds beside `release.sh`, not in it.** The android workload is not in the gate
image (0184), so the Release workflow's `android` job builds it on GitHub's runner,
checking the key and the certificate first. A `publish` job tags and publishes only once
both have passed. `release.sh` run here builds no APK.

## Consequences

- **The certificate is for good.** Android installs an update only over an APK signed
  under the same certificate, so rotating `RELEASE_SIGNING_KEY` now means a signing
  lineage (APK signature scheme v3) for Android as well as a new `release-key.pem` for the
  updater.
- A Release run whose certificate does not pair with the secret fails in the `android`
  job before anything is published, and `AndroidCertificateTests` holds the certificate to
  `release-key.pem` on every push.
- The Flyback updater (0088) does not touch Android; the APK is not in `SHA256SUMS`, and
  its own signature is what Android checks.
- If IzzyOnDroid's policy changes, the release already carries what it needs except the
  Fastlane metadata.
