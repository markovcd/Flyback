// The microphone for a Line In, shared by the viewer and the editor: asked for only while
// the sound that is playing reads one, and let go when it does not (ADR-0178).
//
// It plays into the speaker's worklet, which hands what it hears to the sound's worker.
// Where the browser says no, or the microphone goes, onTrouble says why once and the
// microphone stays shut until want(false) has been said and want(true) said again.

/** The input a stream is asked for: the person's sound as it is, with none of the browser's call processing. */
const RAW = { echoCancellation: false, noiseSuppression: false, autoGainControl: false };

/** Why the browser would not give a microphone, as a person would say it. */
function reason(failure) {
  switch (failure?.name) {
    case 'NotAllowedError':
    case 'SecurityError':
      return 'the browser did not let this page use the microphone.';
    case 'NotFoundError':
    case 'OverconstrainedError':
      return 'no microphone was found.';
    case 'NotReadableError':
      return 'the microphone is in use by something else.';
    default:
      return failure?.message || 'the microphone would not open.';
  }
}

/** Marks the page's sound as recording as well as playing, where the browser has an audio session to set (Safari). */
function session(type) {
  try {
    if (navigator.audioSession) navigator.audioSession.type = type;
  } catch {
    // A browser that refuses the type plays and records as it would have.
  }
}

export class Microphone {
  constructor() {
    this.context = null;
    this.node = null;
    this.stream = null;
    this.source = null;
    this.wanted = false;
    this.asking = false;
    this.refused = false;

    /** Called with a sentence when the microphone will not open or stops. */
    this.onTrouble = () => {};

    /** The last such sentence, until the microphone is let go. */
    this.trouble = null;
  }

  /** Whether the microphone is open and playing into the sound. */
  get listening() {
    return this.stream !== null;
  }

  /** Gives it the speaker's node to play into, opening the microphone at once if it was wanted before there was one. */
  attach(context, node) {
    this.context = context;
    this.node = node;
    if (this.wanted) this.open();
  }

  /** Opens the microphone, or lets it go, to match whether the playing sound reads one. */
  want(on) {
    on = Boolean(on);
    if (on === this.wanted) return;

    this.wanted = on;

    if (on) this.open();
    else this.close();
  }

  async open() {
    if (this.node === null || this.stream !== null || this.asking || this.refused) return;

    this.asking = true;

    try {
      if (!navigator.mediaDevices?.getUserMedia) throw new DOMException('A microphone needs a page served over https.', 'SecurityError');

      session('play-and-record');
      const stream = await navigator.mediaDevices.getUserMedia({ audio: { ...RAW, channelCount: 2, sampleRate: this.context.sampleRate } });

      if (!this.wanted) {
        stream.getTracks().forEach(track => track.stop());
        return;
      }

      this.source = this.context.createMediaStreamSource(stream);
      this.source.connect(this.node);
      this.stream = stream;
      this.node.port.postMessage({ listening: true });

      stream.getAudioTracks()[0]?.addEventListener('ended', () => {
        if (this.stream === stream) this.fail('the microphone stopped.');
      });
    } catch (failure) {
      this.fail(reason(failure));
    } finally {
      this.asking = false;
    }
  }

  /** Lets the microphone go and forgets any trouble, so the next want(true) asks afresh. */
  close() {
    this.release();
    this.refused = false;
    this.trouble = null;
  }

  fail(why) {
    this.release();
    this.refused = true;
    this.trouble = `A Line In is silent: ${why}`;
    this.onTrouble(this.trouble);
  }

  release() {
    this.node?.port.postMessage({ listening: false });
    this.source?.disconnect();
    this.stream?.getTracks().forEach(track => track.stop());
    this.source = null;
    this.stream = null;
    session('playback');
  }
}
