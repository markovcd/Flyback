# ADR-0237: Flyback speaks Ableton Link itself

**Status:** Accepted · 2026-11-16 · *user-directed* · extends
[0139](0139-a-patch-keeps-to-an-instruments-clock.md)

## Context

Ableton Link keeps the apps on one network to one tempo and one beat: Live,
Bitwig, Traktor and hundreds of phone apps speak it. A Clock In could follow a
MIDI clock but nothing on the network, so Flyback could not play along with a
laptop or a phone without a cable.

Ableton's SDK is C++ under the GPL, or under a proprietary licence granted on
request. Flyback is MIT and ships through app stores, the Apple one among them,
which the GPL rules out; binding the SDK also means a native library built for
every platform, against ADR-0019's taste for code we read.

## Decision

**Flyback speaks the protocol in C#, in `Flyback.Host/Ableton`.** It is small:
apps announce their session on a multicast group (224.76.78.75:20808) four
times a second, measure each other's clocks with ping and pong, join the
oldest session, and take the timeline with the latest beat origin and the
transport with the latest timestamp. The wire format and those rules were read
from the protocol's headers; no code was taken. `AbletonSession` is the rules
with no sockets, so two of them play a session in a test; `AbletonHub` runs one
over the network on a thread of its own.

**A Clock In follows it as one more source**, `ableton-link` in `follows`,
writing the same five numbers a MIDI clock writes. The beat is counted from a
bar line of four found on joining, so a patch's bars line up with every other
app's. A session nobody starts or stops is running throughout. A fresh Clock
In never picks it, since following it joins the network.

**Flyback joins only while a playing patch follows the session**, as a MIDI
device is opened only while one is read. It follows; it does not yet lead.

**Desktop and Android.** Android needs a Wi-Fi multicast lock, which its shell
installs. iOS waits for Apple's multicast entitlement, and the page has no UDP.

**`flyback-cli ableton`** joins for a while and prints what it hears, so the
session is checked with no window.

## Consequences

- Link's start and stop and its tempo are followed; Flyback setting the
  session's tempo, and IPv6, are not done.
- Interoperating with Ableton's own apps rests on the protocol staying as it
  is: a peer the SDK adds a field for is passed by, but a change to the rules
  would need following here.
- The name is Ableton's trademark, used to say what Flyback works with.
