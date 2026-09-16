# ADR-0085: A sound backend declares its own settings

**Status:** Accepted · 2026-09-17 · *user-directed* · takes
[0069](0069-an-assistant-declares-its-own-settings.md)'s declared form from the
assistant to the sound backends of [0025](0025-platform-io-behind-loadable-plugins.md)
and [0063](0063-one-plugin-per-platform-for-sound-and-midi.md); adds to the Sound
section [0082](0082-the-output-settings-move-to-the-settings-window.md) opened

## Context

Sound always played through whatever Windows was set to play through. A person
with speakers and headphones, or an interface they want the instrument on while
everything else stays on the laptop, had to change the system's default to move
Flyback, and every other program moved with it.

Which device plays is not a question the shell can ask. The list is a platform's
own — WASAPI endpoint ids, CoreAudio device ids, ALSA card names — and
[0025](0025-platform-io-behind-loadable-plugins.md) exists so that the App holds
none of them. The Sound tab had one control, latency, because latency is the one
thing every backend is asked the same way; a device picker written into the shell
would have been the first platform fact to cross back.

[0069](0069-an-assistant-declares-its-own-settings.md) had already solved the same
shape for the assistant: the plugin declares a list of fields, the App draws it,
and the answers are a bag of strings filed under the plugin's id. It was named for
the assistant — `AssistantField`, `AssistantValues`, `AssistantForm` — but nothing
in it was about assistants.

## Decision

**The vocabulary is the plugins', not the assistant's.** `AssistantField`,
`AssistantOption` and `AssistantValues` become `SettingField`, `SettingOption` and
`SettingValues` in `Flyback.Plugins.Settings`, and the control that draws them
becomes `SettingsForm`. The shapes are unchanged — Text, Pick, Switch — and so is
every rule 0069 set for them, including that a credential is never one.
`AssistantCredential` and `AssistantSenses` stay where they were, since they are
about assistants.

**`IAudioOutput` declares a form.** `Form(SettingValues)` returns the fields,
asked again after every change, and defaults to none — so a backend with nothing
to ask is written exactly as before. Like `IsSupported` it must not open a device
or throw; listing what is plugged in is allowed.

**`Create` is handed the answers.** `Create(AudioFormat, SettingValues)`: the
format is what the host needs, and the bag is what the backend asked for, read back
by the backend. The host stores it and never looks inside.

**WASAPI asks which device plays.** One Pick, `device`, offering *System default*
and every active render endpoint by the name Windows shows. The default is stored
as the word `default` and follows the system's choice as it always did. A device
chosen and then unplugged stays chosen — plugging it back in is all it takes — and
the form says the default plays meanwhile, listing it under a readable name rather
than its endpoint id. The endpoint is looked up each time the sound starts, not
when the device is created, so one plugged in after launch is found.

**The answers are kept in `output.json`, under `sound`, per backend id** — beside
latency, in the file 0082 made for the machine's output choices, and per backend
for the reason 0069 kept them per provider.

**The backend's form comes first on the Sound tab, then latency.** Only the
preferred backend's form is shown, since it is the one that plays.

**Save puts the Sound tab in force at once, latency included.** This reverses
0082's "heard from the next launch". Its reason was that reopening the device
would mean rebuilding the engine under a running audio thread and the clock the
preview follows. It does not: every backend opens hardware in `Start`, not in
`Create`, and the engine depends on its device only for the sample rate. So Save
stops the sound, creates a device from the new answers, hands it to
`AudioEngine.Use` — which keeps the program, its memory and the cursor — and
starts the sound again if Volume wants it. The sound carries on from where it
was, on the new device. A change is judged by what the fields read the answers
as, so a device picked and then picked back reopens nothing.

## Consequences

**A switch is a short gap, not a restart.** The old device is stopped and let go
before the new one starts, so there is a buffer or two of silence, and a
recording running across it has that gap. The picture follows the clock of
whichever device is playing.

**`Use` refuses a device at another rate**, because the renderer's decimation and
every delay line were sized for the rate it was built at. No shipped backend
reports anything but the rate it was asked for, so this does not happen today.
If one does, the old device stays and the new choice is heard from the next
launch, which is said on the status bar.

**A device that will not start says so the way it does at launch**, and saving
another one clears the block that left behind. Before this, only a relaunch did.

**CoreAudio and ALSA declare nothing yet.** Both take the new `Create` and ignore
the bag. Each can grow a picker without the shell changing, which is the point.

**A plugin written against the old names does not build.** There is one
out-of-tree plugin kind this touches — an assistant — and the rename is mechanical.
The contract is host-owned and versioned with the App, so there is no binary to
keep compatible.

**The plugin guide's backend example gains the second parameter.**
