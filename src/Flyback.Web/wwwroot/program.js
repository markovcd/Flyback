// The sound's programs as JsEmitter writes them, made into functions and run on the
// runtime's own memory. JsSound.cs calls these; each program is known by a number.

const programs = new Map();

/**
 * The scripts made lately, by their text, so a program that differs only in its
 * constants, as a knob turned makes one, runs a function the engine has already optimized.
 */
const made = new Map();
const MADE_KEPT = 8;

let views = null;
let next = 1;
let last = '';

/** Takes the functions that hand back the runtime's heap as each typed view: f32, f64, i32 and u8. */
export function attach(heap) {
  views = heap;
}

/** Makes a program from its source and its layout, and answers its number, or nought with error() saying why. */
export function compile(source, layout) {
  try {
    const m = Object.assign(JSON.parse(layout), views);
    const render = make(source)(m);
    const id = next++;

    programs.set(id, render);
    return id;
  } catch (failure) {
    last = String(failure?.message ?? failure);
    return 0;
  }
}

/** The script <source> is the text of, made once and kept among the last few. */
function make(source) {
  let script = made.get(source);

  if (script === undefined) {
    script = new Function(`return ${source}`)();
    if (made.size >= MADE_KEPT) made.delete(made.keys().next().value);
  } else {
    made.delete(source);
  }

  made.set(source, script);
  return script;
}

export const error = () => last;

export function render(id, time, frames, aspect, out) {
  programs.get(id)(time, frames, aspect, out);
}

export function release(id) {
  programs.delete(id);
}
