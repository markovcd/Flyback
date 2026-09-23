# ADR-0094: A run says what it played, and nothing about who played it

**Status:** Accepted · 2026-09-17 · *user-directed* · extends
[0088](0088-a-release-installs-itself-at-the-next-start.md) and
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md)

## Context

Nothing is known about how Flyback is used. A release's download count says how
many copies were fetched and nothing about what happened after: which modules
people reach for, which plugins are installed beside the shell, whether the
assistant is ever asked anything. The instruction was to gather that, and to let
it be switched off in the settings.

Three facts shape how.

**There is nowhere to send it.** The site is static pages on GitHub
([0028](0028-publish-one-platform-at-a-time.md)) and there is no server, no
database and no dashboard anywhere in this project. Writing those is not what
this is for.

**Statistics nobody asked for have to be worth nothing to anybody else.**
Gathering by default is only defensible while a report cannot be tied to a
person or to a machine. A permanent installation id would make every report
pseudonymous rather than anonymous — the thing that has to be asked for rather
than switched off — and would buy retention figures this project has no use for.

**What is being measured is a patch,** and a patch is somebody's work. The same
line that keeps a key out of the settings file
([0034](0034-settings-in-a-file-the-key-in-the-operating-system.md)) and names
the third party an assistant talks to
([0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)) runs
through here: a tally of module types is a fact about Flyback, and anything
finer is a fact about the person.

## Decision

**Reports go to Aptabase,** which counts events for desktop programs, keeps no
address and needs no account from the person running one. The application key
`A-EU-8282660952` is in `src/Flyback.App/Statistics/aptabase-key.txt` and
compiled in, the way the release key is
([0088](0088-a-release-installs-itself-at-the-next-start.md)); the key names its
own region, and a build with no key in it sends nothing and says so on the
terminal. An application key is a public thing — every copy of every program
that uses one carries it — so committing it is not committing a secret.

**Nothing identifies a run but the run.** A session id is made at launch and
never written down, so restarting Flyback is a new one and there is nothing on
disk to join two of them by. No installation id, no machine id, no account, no
locale. What the request itself carries is what any request carries: its
address, which Aptabase turns into a country and does not keep — the same
disclosure [0088](0088-a-release-installs-itself-at-the-next-start.md) makes
about asking GitHub for a release.

**Three kinds of event, and at most five events in a run.**

| Event | When | What it carries |
|---|---|---|
| `started` | once, after the window exists | the platform this was published for, the sound backend that opened, and the id of each plugin Flyback ships that loaded |
| `played` | when the patch starts playing | how many of each module type were in it, and how many modules in all |
| `assistant` | the first message of the run | which provider it went to |

The version and the operating system ride on every event, because Aptabase asks
every event for them.

**A play is reported when it is not the last one again, at most three times.**
Playing is the Volume knob crossing nought
([0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md)), so a run that
reported every one of them would report a drag through zero, and a long evening
of work would be mostly reports. Three is enough to see that a session went
somewhere, and few enough that nobody's afternoon is reconstructable from them.

**Only names Flyback ships are sent, and everything else is `other`** — a plugin
somebody wrote, the modules it adds, an assistant it offers, a sound backend it
registers. The name of a plugin a handful of people have is close enough to a
name for the person running it.

**Nothing about a patch but that tally.** Not its name, not its file, not its
wires, not a knob's value, not a sample or a picture it names, and nothing
typed to an assistant — which is the whole of what the assistant event says as
well: that one was asked, and by which provider.

**A build that is not a release says nothing,** which is the same test the
updater makes: a version with a commit hash after it has no place in the figures
and is somebody working on Flyback rather than using it.

**It is on unless switched off,** in `usage.json` beside the other settings
files, with a Usage tab in the settings window. Switching it off stops the run
it is switched off in; switching it on begins at the next start, because this
run's `started` has already gone unsaid and half a run is worse than none.

**Nothing waits for it and nothing is retried.** Each event is posted on a
thread of its own and whatever comes back goes to the terminal. A report that
does not arrive is lost, which costs one run out of however many; a program that
paused for a statistic would cost the person running it.

## Consequences

**Flyback now goes to a second place online without being asked.** GitHub for a
release, Aptabase for this. Both are named in the settings window, in the tab
that switches each off, and both are on the download page.

**The numbers count runs, not people.** There is no retention figure, no count
of installs, and no way to ask how many people use Flyback — somebody who
restarts it ten times in an evening is ten runs, and somebody who leaves it open
for a week is one. That is the price of a report nobody has to be asked about,
and it is the right way round for a program whose questions are all of the form
"does anyone use the Analyzer".

**The first play of a run is usually the startup preset**
([0093](0093-a-startup-preset-is-a-graphics-setting.md)), which plays itself as
soon as its Volume is up. The module tallies lean toward whatever Flyback opens
on, and reading them means remembering that.

**A repository without the key measures nothing,** so a fork gathers nothing by
accident and nothing has to be stripped out of one.

**What can be seen is what Aptabase shows:** events over time, broken down by
version, operating system and any property on them. Module counts ride as
properties of one event rather than as an event each, because events are what
this is billed by — the cost is that the most-used module is read off a property
list rather than a chart.

**The tests never reach the network.** A window built without a reporter gets
one that says nothing, which is every test but the ones about this; what is said
and what is withheld is tested against a sink that collects, and the wire
against an Aptabase that answers from memory.

## Amendment, 2026-09-24: a build made on a developer's machine counts as debug

A build that is not a release still says nothing, with one exception: a build
made on a developer's machine, which is one that embeds the public half of the
local `RELEASE_SIGNING_KEY` ([0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)).
Its build marks the assembly `LocalBuild`, and its runs are sent under its own
version, commit and all, with Aptabase's `isDebug` set, which the dashboard keeps
apart from the releases. The user wanted local runs visible without mixing them
into the figures. A build on GitHub never carries the mark, so the gate's and
coverage's builds still say nothing, and the Usage switch turns it off the same way.
