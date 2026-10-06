# ADR-0176: The preset site lives at flybackmodular.app alone

**Status:** Accepted · 2026-10-06 · *user-directed* · amends
[0133](0133-a-shared-plugin-is-unpublished-until-the-admin-publishes-it.md) and
[0136](0136-a-letter-goes-to-the-author-through-the-preset-site.md)

## Context

The editor reads the site's address at build time, and every release since 0.5.1 names
`flyback.nasik2137.uk`, a hostname under the author's personal domain. The author bought
`flybackmodular.app` for the project and wants the old address gone.

## Decision

**The website, the API and the admin live at `https://flybackmodular.app/`, and nothing
answers at `flyback.nasik2137.uk`.** The Worker has one custom domain, `PresetSite`
names it, and GitHub Pages holds only a page that sends its old paths there.

## Consequences

- An editor from 0.5.1 to 0.7.1 can no longer reach shared presets, shared plugins or
  the letter box. Its other features do not depend on the site. An update (ADR-0088)
  brings the new address.
- This breaks a published address on purpose, which `saved-data.md` leaves to the user.
  Before 1.0.0 no saved work depends on it.
- The staging copy stays on its own hostname until it is moved too.
