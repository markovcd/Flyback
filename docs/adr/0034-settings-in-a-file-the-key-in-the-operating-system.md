# ADR-0034: Settings in a file, the key in the operating system's store

**Status:** Accepted · 2026-08-16 · *user-directed* · follows
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md), extends
[0025](0025-platform-io-behind-loadable-plugins.md)

## Context

Until [0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)
this program had never configured anything or held a secret. It read no
environment variable, wrote no settings, and knew no path it had not been handed
by a file picker. Every durable thing it produced was a `.fbk` file somebody
chose the location of.

An assistant that works with any provider needs three things remembered: which
provider, which model, and — where the endpoint is the caller's business — which
endpoint. It also needs an API key, and the instruction was explicit: the key is
entered in the application, and it is not to be stored in plain text.

The obvious reading of that is "encrypt the settings file". It is the wrong one.
A file encrypted with a key the application also ships is obfuscated, not
protected: anything that can start Flyback can undo it, and the reassurance is
worth less than the code it takes. Rolling our own is worse — [0019](0019-no-third-party-dependencies-in-the-engine.md)
was willing to hand-write a PNG encoder, and cryptography is not a PNG encoder.

Every operating system already has somewhere for this, unlocked by the login the
person has already done.

## Decision

**Choices go in a file; a credential never does.** `assistant.json` under the
usual per-user application data folder holds provider, model, endpoint, whether
the assistant may look at the picture, and how hard it should think. **There is
no key in it and there is no field one could go in.**

**The key goes to the operating system's own store**, through a new
`ISecretStore` contract: keep, recall, forget, keyed by provider id.

**The store is a plugin.** Windows data protection, the macOS Keychain and the
Linux Secret Service are textbook platform I/O — the case
[0025](0025-platform-io-behind-loadable-plugins.md) exists for, and structurally
the same as the three sound backends: one small plugin each, filtered by the
`Platform` attribute [0028](0028-publish-one-platform-at-a-time.md) already
implements, so a macOS build carries no Windows credential code at all. Putting
`OperatingSystem.IsWindows()` switches in the shell instead would be exactly the
thing 0025 was written to prevent.

**There is no cryptography anywhere in Flyback.** The Windows store hands bytes
to `ProtectedData` under the signed-in account and writes back what it is given.
Nothing here invents a scheme, chooses a cipher, or holds a derived key.

**A key resolves in three steps, and the panel says which one happened:**

1. **An environment variable, when set.** It wins, and it is never written
   anywhere — someone who exports a key has said where it lives, and taking a
   copy would be deciding otherwise on their behalf.
2. **The operating system's store**, when a plugin offers one and the person
   asked for the key to be kept.
3. **This session only**, held in memory and gone with the window.

**A store that fails degrades to the session, never to a readable file.** That is
the one direction this must not fall.

## Consequences

**Only Windows has a store today.** macOS and Linux fall through to the
environment variable or to session-only, and the panel and the plugin summary
say which — because "held for this run" and "saved" look identical until the
next launch, and a program that appeared to save something and did not is worse
than one that never offered. The other two are about forty lines each in the
same shape, shelling out to `security` and `secret-tool`; nothing about this
decision is waiting on them.

**`ProtectedData` is Windows-only and arrives as a package**, so it lives in the
`Platform="win"` plugin and nowhere else, and the type that touches it sits in
its own file — the rule `WasapiPlugin` already follows for NAudio, so the plugin
loads harmlessly on a machine it cannot work on and answers `IsSupported`
without ever resolving it.

**The platform analyser cannot see through a helper**, so the Windows guard is
written out at all three call sites rather than shared. It looks like something
worth tidying up and is not; there is a comment there saying so.

**The settings file is not load-bearing.** Missing, unreadable, or written by a
later version all mean the defaults. Losing a preference is not worth failing to
start over, and this is the first file the program has ever depended on at all.

**A test fails if anyone adds somewhere a secret could go.** `AssistantSettings`
is checked by reflection for a string property whose name looks like a
credential. That turns this record from a thing somebody has to remember into a
thing the build enforces — which matters because the pressure to "just add an
ApiKey field" will be real and will look harmless.

**Installing a store means restarting**, because [0025](0025-platform-io-behind-loadable-plugins.md)
does not unload plugins. Until one is installed a key is retyped each launch,
which is tolerable precisely because the panel says that is what will happen.

**A blob that cannot be decrypted is treated as no key.** Written by a different
account, copied from another machine, or corrupted — none of it is something
anybody can act on, so the panel asks for a key rather than reporting a fault.

What this does not do: there is no passphrase option for a machine with no
keyring, so headless Linux is environment-variable-or-nothing; nothing is stored
in the Windows Credential Manager proper, only protected on disk beside the
settings; and a key is held in memory as an ordinary string for as long as the
window is open, which is true of every program that has ever sent one.
