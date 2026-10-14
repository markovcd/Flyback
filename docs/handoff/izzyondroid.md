# Publishing the Android editor on IzzyOnDroid

Written on 2026-10-14, on `main` at `8fd4294`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the listing.

- **Kind:** Plan
- **Status:** Open, parked

## Why IzzyOnDroid and not F-Droid

F-Droid builds every app from source on Debian, and no .NET app is in its repository. Its
maintainers say .NET cannot be bootstrapped there; their fallback is Ubuntu's packaged
SDK, and a distribution's SDK takes no workloads, so the android workload is out of reach
(ADR-0184). SkiaSharp's and HarfBuzz's native libraries would also have to be built from
source. See the [forum thread](https://forum.f-droid.org/t/why-isnt-c-net-maui-supported/24842)
and [dotnet/core#10103](https://github.com/dotnet/core/issues/10103).

[IzzyOnDroid](https://izzyondroid.org/docs/general/AppInclusionPolicy/) lists APKs the
developer builds and signs, taken from GitHub releases.

## What already passes

- MIT license.
- No Google Play services: `Avalonia.Android` pulls in AndroidX only.
- No tracking: `DeviceApp` leaves `EditorSetup.Usage` at `Usage.Off`.
- No assistant: the plugins that wrap a CLI are not linked in (ADR-0184).
- The only network use is the preset site, whose Worker is in this repository, so
  NonFreeNet should not apply.

## What it takes

1. **An APK on each GitHub release.** `release.sh` and `release.yml` ship none today.
2. **A release keystore.** CI's `SignAndroidPackage` signs with the debug key, which
   IzzyOnDroid refuses. The keystore and its password are secrets, held like
   `RELEASE_SIGNING_KEY` and never committed.
3. **A rising version code.** `ApplicationVersion` is fixed at 1 in
   `Flyback.Editor.Android.csproj`; derive it from the tag, and `ApplicationDisplayVersion`
   from the version `release.sh` already computes.
4. **Under 30 MB.** The CI APK for both arm64 and x64 is 43 MB. An arm64-only APK should fit;
   not measured.
5. **Metadata:** Fastlane-style description and screenshots in the repository, then a
   request on IzzyOnDroid's Codeberg tracker.

Reproducible builds would earn IzzyOnDroid's verified badge, and are the road to F-Droid
should it ever build .NET.
