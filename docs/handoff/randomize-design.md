# Design brief: randomizing the knobs

Written on 2026-11-02, on `main` at `25b8e18`, for Claude Design. It is on TODO.md; take it off there, and delete this file, in the commit that lands the redesign.

Flyback is a patchable synthesizer: one patch of modules wired together makes both a picture and a sound. A patch can pin a row of **panel knobs** under the canvas (the **knob panel**) to perform it by hand or from a MIDI controller. **Randomize** sends those knobs somewhere new so the player can hear and see where the patch can go, then keep it or go back. This brief asks for a redesign of the randomize controls, which today are small, unlabeled and easy to misread.

Reference shot: `panel-today.png` in the project files (`randomize/`), the knob panel in the Graphite scheme with one knob (taken before Phosphor was the default).

## The words

Use these in anything the user sees: **randomize** (not shuffle, roll or dice), **back** (returns the knobs to where they were), **held** (a knob that sits a randomize out; not locked), **panel knob**, **knob panel**, **module**, **socket**, **wire**, **picture** and **sound**. The app's own menu says "Roll the Panel's Knobs" today, which the design may correct.

## How it works today

There are two randomizes, with different rules.

**The panel's randomize** (ADR-0170, `KnobRandomizer`):

- Fired by the die button at the end of the knob panel, Ctrl+Shift+K (also while the picture has the window), or a controller button or pad learned for it.
- Every panel knob that is not held and is not a button goes somewhere new. A knob lands evenly within **amount** of where it is (amount 1 means anywhere on it). A switch flips with the amount as its chance.
- The knobs **glide** there, eased, over 0 to 8 s (0 jumps). A hand or a controller that takes a gliding knob takes it out of the glide.
- It is not an undo step: turning a knob never is, so Ctrl+Z does nothing to it. Instead **back** (the arrow under the die) glides the knobs to where they were before the last randomize, up to 32 deep. There is no forward. Opening another patch forgets the history.
- **Held** is saved with the patch. It is set per knob from the knob's "…" menu ("Hold when randomizing"); a held knob shows only as a dimmer name.
- **Amount**, **glide** and the learned controller trigger are the player's, saved in settings, not the patch. Amount and glide are the two small knobs beside the die. The trigger is learned or forgotten from the die's right-click menu.
- The whole cell is hidden when the panel has no knobs.

**The module's randomize** (ADR-0209, `ModuleDie`):

- Fired by a die in the selected module's inspector action row, or Ctrl+Shift+D.
- Every unwired socket on the selected modules that rests on a value of its own goes somewhere within the same **amount**; a stepped socket lands on a whole step. A wired socket, or one linked to a panel knob, stays.
- It jumps (no glide) and **is** one undo step: Ctrl+Z and Ctrl+Y step through the results, which is how a player compares them.

## What is wrong, from the screenshot and the code

1. **The die and back are the smallest things on the panel.** Two ~16 px glyphs stacked in a column, beside a 44 px "+" and 30 px knobs. Neither has a label.
2. **Back looks like Undo.** It is the app's undo arrow, yet it is not undo, and Ctrl+Z does not do what it does. A player who presses Ctrl+Z after a randomize gets an unrelated edit undone.
3. **Amount and glide look like patch knobs.** They are drawn as exactly the same orange-arc knob as "Knob 1", just smaller, so they read as two more knobs of the patch rather than settings of the randomize.
4. **No values.** 100% and 0.5 s live only in tooltips, which touch never shows.
5. **It doesn't read as one tool.** The "+" (add a knob) sits between the patch's knobs and the randomize controls, so nothing groups the die, back, amount and glide, or sets them apart from the patch's own row.
6. **Nothing says what will move.** Held knobs are only dimmer names, and holding is two clicks deep in each knob's menu. Before pressing the die, there's no way to see which knobs it'll touch.
7. **The controller trigger is hidden.** Learning it is a right-click on the die, and nothing shows that one is learned or which.
8. **Two randomizes, one look, different rules.** The panel's and the module's share the die icon and the amount, but one glides and has its own back, and the other jumps and is an undo step. The module's amount can only be set on the panel, which is hidden when the patch has no panel knobs (confirmed by reading `ControlsPanel.Rebuild`).
9. **Touch.** The glyph buttons and 30 px knobs are well under a 44 px target, and right-click learn has no obvious touch path.

## What to design

A set of artboards showing one coherent randomize:

1. **The panel at rest**, with 3 to 8 knobs, one of them held. The randomize controls read as one tool, set apart from the patch's knobs, with amount and glide visible as values and not mistakable for panel knobs.
2. **Held.** How a held knob looks at a glance, and a quicker way to hold or release one (for example a toggle on the knob, or a hold mode where tapping knobs toggles them).
3. **Before pressing:** an optional preview of which knobs will move (on hover or focus of the die).
4. **While gliding:** the knobs moving, how the panel shows a glide in progress, and what grabbing one knob mid-glide looks like.
5. **Back:** an icon and label that don't read as Undo, with how many steps are left (up to 32). Consider whether a forward belongs beside it.
6. **The controller trigger:** learning one, showing that one is learned (for example "Pad 5" under the die), forgetting it.
7. **The module's randomize** in the inspector: how it relates to the panel's. Either make the difference visible (it's an undo step, Ctrl+Z steps through the results) or propose one model for both. Amount must be settable without panel knobs.
8. **Empty panel:** what shows when the patch has no panel knobs.
9. **Touch:** a tablet and a phone (the panel wraps into a grid when narrow). Targets at least 44 px; learn and hold reachable without right-click or hover.

## Constraints

- **Real controls.** The knob panel is built from Avalonia controls (buttons, a drawn `Knob` control, text), not painted like the canvas, so ordinary controls, menus and flyouts are fine.
- **Space.** The panel is one row of knob cells under the canvas (a 44 px "+" is its tallest control), wrapping to more rows when there are many or the window is narrow. The randomize tool must not take more width than two knob cells.
- **Colors** come from the scheme. Phosphor is the default:

  | Use | Hex |
  |---|---|
  | Window | #16181B |
  | Canvas | #1A1C20 |
  | Panel | #1C1E22 |
  | Toolbar | #22252A |
  | Module | #2A2D34 |
  | Separator | #3A3E46 |
  | Label | #C8CCD4 |
  | Value | #8A92A0 |
  | Muted | #8A909A |
  | Inactive | #5A606A |
  | Bright | #F2F4F8 |
  | Accent | #77EA9A |

  The design must hold in the other schemes too (Amber, Graphite, Midnight, Paper, which is light). No new accent color: picking one is the user's call. Avoid the generic dark-UI-with-amber look; the app is moving away from it.
- **Type.** System sans; small labels 11 px, body 12 px, titles 17 px.
- **Drivable without eyes.** Flyback is built and tested by an AI assistant, so every state the design adds (held, a learned trigger, back's depth, a glide in progress) must also be reachable by a key or a command and readable as text, not only by hover or color. Today there is no command-line or MCP way to randomize or to read held; a design that names its keys is a plus.
- **Accessibility.** Text contrast at least 4.5:1 on the panel. Held and not held must differ by more than hue.

## Out of scope

How a randomize picks its values (even within amount), the 32-step history, and held being saved with the patch are settled by ADR-0170 and ADR-0209. Propose changes to them separately if the design needs one.
