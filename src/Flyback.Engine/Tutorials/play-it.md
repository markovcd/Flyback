# Play it

From: Empty

A saw you play from the computer keyboard, each note shaped by an envelope.

## A saw to play

Add a Saw and wire its out into the Output's left.

```fbks
saw() |> out.left
```

## Keys in

Add a MIDI In and a Note. Wire the MIDI In's pitch into the Note's note, and the Note's hz into the Saw's freq. Play the computer keyboard, or show the keys with the toolbar's keys button: the Saw follows whichever key went down last.

```fbks
let keys = midi.in()
keys.pitch |> note(note: _) |> saw(freq: _) |> out.left
```

## Shape each note

Add an ADSR. Wire the MIDI In's gate into its gate, and its out into the Saw's amp. Now a note sounds only while its key is held, and fades once it is let go.

```fbks
let keys = midi.in()
let shape = keys.gate |> adsr(gate: _)
keys.pitch |> note(note: _) |> saw(freq: _, amp: shape) |> out.left
```

## Make it pluck

Turn the ADSR's decay to 200 ms and its sustain to 0.2. Each note strikes, then settles quietly while the key is held.

```fbks
let keys = midi.in()
let shape = keys.gate |> adsr(gate: _, decay: 200ms, sustain: 0.2)
keys.pitch |> note(note: _) |> saw(freq: _, amp: shape) |> out.left
```
