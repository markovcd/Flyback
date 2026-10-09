// The page end of the speakers, shared by the viewer and the editor: the sound's worker
// (speaker.js), the worklet that plays what it renders (sound.js) fed straight from it, the
// microphone for a Line In, the volume, and the clock. While the speakers are heard, how far
// they have played is the clock; until then the wall clock stands in. When the sound joins
// is each page's own call.

import { Microphone } from './microphone.js';
import { takeAudio, throughElement } from './session.js';

/** How long a resume is waited on before the browser is taken to be holding the sound back. */
const RESUME_WAIT = 250;

export class Speakers {
  /** The sound's worker; the page hears what it says. */
  worker = new Worker(new URL('./speaker.js', import.meta.url), { type: 'module' });

  /** The microphone, held open for a Line In while the page asks for it. */
  microphone = new Microphone();

  /** Called whenever the browser lets the speakers play, or stops them. */
  onState = () => {};

  #context = null;
  #volume = null;
  #loudness = 1;

  /** The <audio> element the sound plays through where the page must take audio focus, else null. */
  #element = null;

  /** Resolves once the worklet is fed by the worker. */
  #opening = null;
  #attached = false;

  #running = false;
  #heard = false;
  #generation = 0;
  #origin = 0;
  #held = 0;
  #wallStart = 0;
  #played = 0;
  #reportedAt = 0;

  /** Whether the clock runs. */
  get running() {
    return this.#running;
  }

  /** Whether the speakers are the clock: only while the queue they play starts where the picture is. */
  get heard() {
    return this.#heard;
  }

  /** Whether the worklet is there and fed. */
  get attached() {
    return this.#attached;
  }

  /** The browser's word on the speakers, or null before they are opened. */
  get state() {
    return this.#context?.state ?? null;
  }

  /** Where the patch is, in seconds: the speakers' position while heard, the wall clock otherwise. */
  time() {
    if (!this.#running) return this.#held;

    if (this.#heard) {
      const since = this.#played > 0 ? Math.min(Math.max(this.#context.currentTime - this.#reportedAt, 0), 0.1) : 0;
      this.#held = this.#origin + this.#played / this.#context.sampleRate + since;
    } else {
      this.#held = this.#origin + (performance.now() - this.#wallStart) / 1000;
    }

    return this.#held;
  }

  /** The clock and the worker to <seconds>, the speakers' queue emptied and counted again from there. */
  seek(seconds) {
    this.#generation++;
    this.#origin = this.#held = seconds;
    this.#played = 0;
    this.#wallStart = performance.now();
    this.worker.postMessage({ seek: seconds, generation: this.#generation });
  }

  /** Runs the clock: on the speakers when <heard>, where a queue left by a stop carries on; on the wall clock from where it was held otherwise. */
  start(heard) {
    this.#heard = heard;

    if (!heard) {
      this.#origin = this.#held;
      this.#wallStart = performance.now();
    }

    this.#running = true;
    this.worker.postMessage({ run: heard });
  }

  /** Holds the clock at <at>, where it is unless given; the queue stays, so a start carries on from the very next sample. */
  stop(at = this.time()) {
    this.#held = at;
    this.#running = false;
    this.worker.postMessage({ run: false });
    if (this.#heard) this.#context.suspend();
    this.#element?.pause();
  }

  /** Forgets the speakers' queue, so the next start seeks both halves to one moment. */
  drop() {
    this.#heard = false;
  }

  /** Makes the speakers at <rate> on the first call, and resolves once the worker feeds them. */
  open(rate) {
    if (this.#opening !== null) return this.#opening;

    takeAudio();
    const context = this.#context = new AudioContext({ sampleRate: rate, latencyHint: 'interactive' });
    context.onstatechange = () => this.onState();
    this.#volume = new GainNode(context, { gain: this.#loudness });
    this.#element = throughElement(context, this.#volume);
    if (this.#element === null) this.#volume.connect(context.destination);

    this.#opening = context.audioWorklet.addModule(new URL('./sound.js', import.meta.url)).then(() => {
      const queue = new AudioWorkletNode(context, 'flyback-queue', { outputChannelCount: [2], channelCount: 2, channelCountMode: 'explicit' });
      queue.connect(this.#volume);
      this.microphone.attach(context, queue);
      queue.port.onmessage = ({ data }) => this.#report(data);

      // The worker feeds the speakers straight, so a busy page never keeps the sound waiting.
      const channel = new MessageChannel();
      queue.port.postMessage({ feed: channel.port1 }, [channel.port1]);
      this.worker.postMessage({ speaker: channel.port2 }, [channel.port2]);

      this.#attached = true;
    });

    return this.#opening;
  }

  /**
   * Opens the speakers at <rate> if they are not, asks the browser to let them play, and
   * says whether it did: it holds sound back until the page is used. Asks at once, so a
   * call from a press counts as the page being used.
   */
  async resume(rate) {
    const opening = this.open(rate);
    const resuming = this.#context.resume().catch(() => {});

    await opening;
    await Promise.race([resuming, new Promise(resolve => setTimeout(resolve, RESUME_WAIT))]);

    const playing = this.#context.state === 'running';
    if (playing) this.#element?.play().catch(() => {});

    return playing;
  }

  /** Sets how loud the speakers play, 0 to 1. */
  gain(level) {
    this.#loudness = level;
    if (this.#volume !== null) this.#volume.gain.value = level;
  }

  /** What a script driving the page can read of the speakers. */
  status() {
    return {
      running: this.#running, heard: this.#heard,
      context: this.state,
      time: this.time(), played: this.#played, generation: this.#generation,
      microphone: { wanted: this.microphone.wanted, listening: this.microphone.listening, trouble: this.microphone.trouble },
    };
  }

  /** The speakers' count of what they have played, which is the clock while the sound is heard. */
  #report(data) {
    if (data.generation !== this.#generation) return;

    this.#played = data.played;
    this.#reportedAt = data.at;
  }
}
