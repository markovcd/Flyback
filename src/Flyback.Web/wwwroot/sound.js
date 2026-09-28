// The speaker end of the sound: plays the interleaved stereo chunks the page renders,
// and says how many frames it has played and how often it ran dry.

class Queue extends AudioWorkletProcessor {
  constructor() {
    super();
    this.chunks = [];
    this.offset = 0;
    this.played = 0;
    this.starved = 0;
    this.generation = 0;
    this.quanta = 0;

    this.port.onmessage = ({ data }) => {
      if (data.clear !== undefined) {
        this.chunks = [];
        this.offset = 0;
        this.played = 0;
        this.generation = data.clear;
        return;
      }

      this.chunks.push(data);
    };
  }

  process(inputs, outputs) {
    const [left, right] = outputs[0];
    const frames = left.length;
    let i = 0;

    while (i < frames && this.chunks.length > 0) {
      const chunk = this.chunks[0];

      while (i < frames && this.offset < chunk.length) {
        left[i] = chunk[this.offset];
        right[i] = chunk[this.offset + 1];
        this.offset += 2;
        i++;
      }

      if (this.offset >= chunk.length) {
        this.chunks.shift();
        this.offset = 0;
      }
    }

    // A dry spell before the first chunk is the queue filling, not the page falling behind.
    if (i < frames && this.played > 0) this.starved++;

    this.played += i;
    left.fill(0, i);
    right.fill(0, i);

    if (++this.quanta % 8 === 0)
      this.port.postMessage({ played: this.played, starved: this.starved, generation: this.generation, at: currentTime });

    return true;
  }
}

registerProcessor('flyback-queue', Queue);
