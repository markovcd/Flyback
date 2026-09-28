// The GL calls WebGl.cs makes, on one WebGL 2 context. GL names are integers on the
// C# side, as they are on the desktop; this keeps the objects they stand for.

const COMPLETION_STATUS_KHR = 0x91B1;
const PIXEL_PACK_BUFFER = 0x88EB;
const UNSIGNED_BYTE = 0x1401;
const HALF_FLOAT = 0x140B;
const FLOAT = 0x1406;

let gl = null;
let heap = null;
let parallel = null;
let packBuffer = 0;

const objects = new Map([[0, null]]);
const uniformsOf = new Map();
let next = 1;

function keep(object) {
  const name = next++;
  objects.set(name, object);
  return name;
}

const get = name => objects.get(name) ?? null;

function drop(name) {
  objects.delete(name);
}

/** Binds these calls to a canvas, and to a function returning the runtime's heap as bytes. */
export function attach(canvas, heapU8) {
  gl = canvas.getContext('webgl2', { antialias: false, alpha: false, depth: false, stencil: false, preserveDrawingBuffer: true });
  if (!gl) return 'This browser has no WebGL 2.';

  heap = heapU8;
  gl.getExtension('EXT_color_buffer_float');
  gl.getExtension('EXT_color_buffer_half_float');
  gl.getExtension('OES_texture_float_linear');

  return null;
}

export const context = () => gl;

function pixels(type, pointer, count) {
  const bytes = heap();
  if (type === FLOAT) return new Float32Array(bytes.buffer, pointer, count);
  if (type === HALF_FLOAT) return new Uint16Array(bytes.buffer, pointer, count);
  return new Uint8Array(bytes.buffer, pointer, count);
}

export const getError = () => gl.getError();

export function supports(extension) {
  const name = extension.replace(/^GL_/, '');
  const found = gl.getExtension(name) !== null;
  if (found && name === 'KHR_parallel_shader_compile') parallel = true;
  return found;
}

export const disable = capability => gl.disable(capability);
export const viewport = (x, y, width, height) => gl.viewport(x, y, width, height);
export const clearColor = (r, g, b, a) => gl.clearColor(r, g, b, a);
export const clear = mask => gl.clear(mask);

export const genTexture = () => keep(gl.createTexture());
export function deleteTexture(name) { gl.deleteTexture(get(name)); drop(name); }
export const bindTexture = (target, name) => gl.bindTexture(target, get(name));

export function texImage2D(target, level, internalFormat, width, height, format, type, pointer) {
  const data = pointer === 0 ? null : pixels(type, pointer, width * height * 4);
  gl.texImage2D(target, level, internalFormat, width, height, 0, format, type, data);
}

export const texParameteri = (target, name, value) => gl.texParameteri(target, name, value);
export const activeTexture = unit => gl.activeTexture(unit);

export const genFramebuffer = () => keep(gl.createFramebuffer());
export function deleteFramebuffer(name) { gl.deleteFramebuffer(get(name)); drop(name); }
export const bindFramebuffer = (target, name) => gl.bindFramebuffer(target, get(name));

export const framebufferTexture2D = (target, attachment, textureTarget, texture, level) =>
  gl.framebufferTexture2D(target, attachment, textureTarget, get(texture), level);

export const checkFramebufferStatus = target => gl.checkFramebufferStatus(target);

export const blitFramebuffer = (sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, mask, filter) =>
  gl.blitFramebuffer(sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, mask, filter);

export const readBuffer = source => gl.readBuffer(source);

export function drawBuffers(count, pointer) {
  gl.drawBuffers(Array.from(new Int32Array(heap().buffer, pointer, count)));
}

export function readPixels(x, y, width, height, format, type, pointer) {
  if (packBuffer !== 0) gl.readPixels(x, y, width, height, format, type, pointer);
  else gl.readPixels(x, y, width, height, format, type, pixels(type, pointer, width * height * 4));
}

export const genBuffer = () => keep(gl.createBuffer());
export function deleteBuffer(name) { gl.deleteBuffer(get(name)); drop(name); }

export function bindBuffer(target, name) {
  if (target === PIXEL_PACK_BUFFER) packBuffer = name;
  gl.bindBuffer(target, get(name));
}

export function bufferData(target, size, pointer, usage) {
  if (pointer === 0) gl.bufferData(target, size, usage);
  else gl.bufferData(target, new Uint8Array(heap().buffer, pointer, size), usage);
}

export const genVertexArray = () => keep(gl.createVertexArray());
export const bindVertexArray = name => gl.bindVertexArray(get(name));
export function deleteVertexArray(name) { gl.deleteVertexArray(get(name)); drop(name); }

export const createShader = type => keep(gl.createShader(type));
export const shaderSource = (shader, source) => gl.shaderSource(get(shader), source);
export const compileShader = shader => gl.compileShader(get(shader));
export function deleteShader(shader) { gl.deleteShader(get(shader)); drop(shader); }
export const getShader = (shader, name) => Number(gl.getShaderParameter(get(shader), name));
export const shaderLog = shader => gl.getShaderInfoLog(get(shader)) ?? '';

export const createProgram = () => keep(gl.createProgram());
export const attachShader = (program, shader) => gl.attachShader(get(program), get(shader));
export const linkProgram = program => gl.linkProgram(get(program));

export function deleteProgram(program) {
  gl.deleteProgram(get(program));
  drop(program);
  for (const location of uniformsOf.get(program) ?? []) drop(location);
  uniformsOf.delete(program);
}

export function getProgram(program, name) {
  // Without the extension a link is always finished by the time anything asks.
  if (name === COMPLETION_STATUS_KHR && !parallel) return 1;
  return Number(gl.getProgramParameter(get(program), name));
}

export const programLog = program => gl.getProgramInfoLog(get(program)) ?? '';
export const useProgram = program => gl.useProgram(get(program));

export function getUniformLocation(program, name) {
  const location = gl.getUniformLocation(get(program), name);
  if (location === null) return -1;

  const handle = keep(location);
  if (!uniformsOf.has(program)) uniformsOf.set(program, []);
  uniformsOf.get(program).push(handle);
  return handle;
}

export const uniform1f = (location, value) => gl.uniform1f(get(location), value);
export const uniform1i = (location, value) => gl.uniform1i(get(location), value);

export const drawArrays = (mode, first, count) => gl.drawArrays(mode, first, count);
