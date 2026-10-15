# Flyback Modular on the Microsoft Store

Written on 2026-10-15, on `main` at `4231983`. It is on TODO.md; take it off there, and
delete this file, in the commit that adds the Store badge to the site.

- **Kind:** Plan
- **Status:** Open: the repo's part landed (ADR-0199); the listing waits on the user

## What is done

- A free individual developer account, and the name **Flyback Modular** reserved. "Flyback"
  is reserved by someone else. The identity (`PotatoSmasher.FlybackModular`, publisher
  `CN=DEA73A02-E076-4B08-BD88-B3BCFC24BA5B`, "Potato Smasher") is in
  `src/Flyback.Editor.Desktop/Store/AppxManifest.xml`.
- Each Release runs `store.yml`, which packs the MSIX, installs and checks it on a clean
  Windows runner, and attaches it to the release. For an existing release, run
  **Store package** by hand with its tag, and tick *attach* to attach the package.

## What is left, in order

1. **Release** a version with ADR-0199 in it, or run Store package on one, and download
   `flyback-<version>-win-x64.msix` from the release.
2. **In Partner Center**, under Flyback Modular, start a submission:
   - **Pricing:** free; markets: all.
   - **Properties:** category *Music*, subcategory none; the privacy policy URL (step 3).
   - **Age ratings:** the questionnaire. Flyback has no user chat, purchases or ads; the
     assistant sends a patch to a model the user picked themselves.
   - **Packages:** upload the `.msix`.
   - **Store listing (English):** description, at least one screenshot (1366×768 or
     larger; `site/assets/shots` has them), the search terms.
   - **Submission options → restricted capabilities:** justify `runFullTrust` (a desktop
     program) and `unvirtualizedResources` (settings and plugins in `%AppData%\Flyback`,
     shared with the command line, a zip copy and the assistant's own program; ADR-0199).
3. **A privacy policy page.** Done: https://flybackmodular.app/privacy.html, the URL for
   the Properties page.
4. **Submit**, and wait for certification. A rejection names the policy it failed.
5. **Once live:** a "Get it from Microsoft" badge on the site's download section, linking
   Store ID `9P89P2K38TWB`, in the same commit that deletes this file.

## Known gaps in a Store copy

- Settings → Files "Open with" changes nothing; the manifest opens files in the editor.
- `flyback-cli plugin list` lists the installed folder only, not the shipped plugins.
