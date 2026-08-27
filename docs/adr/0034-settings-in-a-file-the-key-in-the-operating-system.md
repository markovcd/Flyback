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

1. **This session**, when somebody has typed one into the panel. Entering a key
   is a deliberate act; an exported variable is the room they are standing in,
   and the deliberate act wins.
2. **The operating system's store**, when a plugin offers one and the person
   asked for the key to be kept. Below the session because `Accept` writes both
   when it is asked to keep — so they agree whenever they can, and where they
   disagree it is because a key was just typed and *not* kept, which makes the
   typed one the newer.
3. **An environment variable, when set.** It is never written anywhere — someone
   who exports a key has said where it lives, and taking a copy would be
   deciding otherwise on their behalf.

**Amended.** The environment was first, and was tried that way. On a machine
exporting a key, typing one into the settings had no effect whatever: the field
emptied itself, the panel went on naming the variable, and nothing available
from inside the application could change which key was sent. That is not a
disclosure problem to be worded around — the settings field was simply inert,
and it is the only key entry point the program has.

Preferring an entered key costs the original rule nothing, because the rule that
mattered was *never write the variable back*, not *always obey it*. The variable
is still never copied, and `Forget` drops straight back to it — which is what
makes preferring an entered key safe rather than a trap, and why one bad key
typed in is not permanent on a machine whose variable was right all along.

**A store that fails degrades to the session, never to a readable file.** That is
the one direction this must not fall.

## Consequences

**Where there is no store, a key is held for the run and the panel says so** —
because "held for this run" and "saved" look identical until the next launch,
and a program that appeared to save something and did not is worse than one that
never offered.

**Amended: all three platforms have a store.** This shipped with only the
Windows one, and said the other two would be about forty lines each in the same
shape, shelling out to `security` and `secret-tool`. They are, and they do:
`flyback.keychain` under `Platform="osx"` and `flyback.keyring` under
`Platform="linux"`, each one class that offers the store and one that talks to
the machine, exactly as `DpapiPlugin` and `Vault` are. Nothing in the shell
changed to accept them — the panel already named whichever store it was handed,
which is what 0025 is for.

The command line rather than the library, in both cases and for the same reason
in neither. On macOS the Security framework would mean CoreFoundation
dictionaries, constants read out by symbol and a lifetime rule per object, for
three operations, in the one place where a bug is a disclosure; on Linux
libsecret is GLib all the way down, and `secret-tool` is the front that library
itself ships for this. Both tools come with the system that has the store.

**The macOS store passes the key as an argument, and that is the best of the
options.** For the length of one call another program run by this same user
could read it out of the process list — and that same program could ask
`security` for the key outright, so it widens nothing. The alternative is worse:
`security` asked for a password it was not given reads it from the terminal, so
a Flyback started from one would hang instead of saving. The Linux store has no
such trade to make; `secret-tool` takes the secret on standard input.

**The Linux store asks three questions where the others ask one.** Being on
Linux is not enough: `secret-tool` has to be installed, and there has to be a
session bus for it to reach a keyring over. A server or a container frequently
has the first and not the second, and that is exactly the machine where a key
would otherwise appear to have been saved and not be there next time.

**`ProtectedData` is Windows-only and arrives as a package**, so it lives in the
`Platform="win"` plugin and nowhere else, and the type that touches it sits in
its own file — the rule `WasapiPlugin` already follows for NAudio, so the plugin
loads harmlessly on a machine it cannot work on and answers `IsSupported`
without ever resolving it.

**The platform analyser cannot see through a helper**, so the Windows guard is
written out at all three call sites rather than shared. It looks like something
worth tidying up and is not; there is a comment there saying so. The other two
stores do share theirs, because starting a program is not an API for one
operating system and there is no analyser to satisfy — which is a difference a
reader will notice, so each of them says why.

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
A keychain that is locked and a keyring that is not answering are read the same
way, for the same reason. Keeping one is the other direction and does fail
loudly: `Credentials` catches it, the key still works for this run, and the
panel reads the source back rather than trusting that a write worked.

What this does not do: there is no passphrase option for a machine with no
keyring, so headless Linux is environment-variable-or-nothing; nothing is stored
in the Windows Credential Manager proper, only protected on disk beside the
settings; and a key is held in memory as an ordinary string for as long as the
window is open, which is true of every program that has ever sent one.
