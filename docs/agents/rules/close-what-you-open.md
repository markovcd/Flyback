# Close what you open

## Nothing you started stays running on the user's machines

Anything a session starts on the user's own machines is closed when the work that needed it is done: an editor window, an iOS simulator, an Android emulator, the app on a phone, the Simulator or emulator app itself, a dev server, a background build or test host. Close it before the reply that reports the result, not later.

**Why:** the user asked for it outright, and asked again when an iOS simulator was left booted on the Mac after the run that needed it. A machine is the user's to work on; a simulator left behind costs memory and battery, and a phone left playing a heavy patch overheats.

**How to apply:**

- This holds for every way of reaching a machine: a shell, Remote Control, folder access or computer use. A session briefed to work on the user's machine is given this rule in its brief.
- Close only what this session started, by its process id, device id or window, never "every Flyback" or "all simulators" when something else may be running. `xcrun simctl shutdown <device>` and quitting Simulator.app for a simulator this session booted; `adb emu kill` or `adb shell am force-stop <package>` for an emulator or phone.
- A script that leaves something running for the next run (`scripts/ios-smoke.sh` keeps a simulator it found booted) does not change whose it is: if this session booted it, this session shuts it down.
- When the user asked to see something, leave it up for them, say in the reply what is still open and how it closes, and close it when they say they are done or the thread's work ends, whichever comes first.
- What could not be closed is named in the reply, with the command that closes it.
