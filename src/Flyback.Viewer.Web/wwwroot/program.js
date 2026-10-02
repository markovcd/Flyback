// The sound's programs as JsEmitter writes them, made into functions and run on the
// runtime's own memory. JsSound.cs calls these; each program is known by a number.

const programs = new Map();

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
    const id = next++;
    const render = make(source, id)(m);

    programs.set(id, render);
    return id;
  } catch (failure) {
    last = String(failure?.message ?? failure);
    return 0;
  }
}

/**
 * The script <source> is the text of, as a function of its own. The engine optimizes a
 * function made twice from one text for neither, which runs it a third slower, so the
 * text is made unique by the program's number; a knob turned is a retune instead.
 */
function make(source, id) {
  return new Function(`return ${source}\n// ${id}`)();
}

export const error = () => last;

/** Hands program <id> new values for its constants, in place. */
export function retune(id, constants) {
  programs.get(id).retune(constants);
}

export function render(id, time, frames, aspect, out) {
  programs.get(id)(time, frames, aspect, out);
}

export function release(id) {
  programs.delete(id);
}
