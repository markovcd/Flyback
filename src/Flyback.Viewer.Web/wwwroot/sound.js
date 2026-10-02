// The speaker end of the sound: plays the interleaved stereo chunks the sound's worker
// renders, and says how many frames it has played and how often it ran dry, to the page
// for its clock and to the worker for its queue.
//
// The page hands it { feed: port }, the worker's end of a channel. On the feed come
// { clear } and { generation, samples }; a chunk from before the last clear is dropped.

class Queue extends AudioWorkletProcessor {
  constructor() {
    super();
    this.chunks = [];
    this.offset = 0;
    this.played = 0;
    this.generation = 0;
    this.quanta = 0;
    this.feed = null;

    this.port.onmessage = ({ data }) => {
      if (data.feed === undefined) return;

      this.feed = data.feed;
      this.feed.onmessage = ({ data: fed }) => this.take(fed);
    };
  }

  take(data) {
    if (data.clear !== undefined) {
      this.chunks = [];
      this.offset = 0;
      this.played = 0;
      this.generation = data.clear;
      return;
    }

    if (data.generation === this.generation) this.chunks.push(data.samples);
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

    this.played += i;
    left.fill(0, i);
    right.fill(0, i);

    if (++this.quanta % 8 === 0) {
      const report = { played: this.played, generation: this.generation, at: currentTime };
      this.port.postMessage(report);
      this.feed?.postMessage(report);
    }

    return true;
  }
}

registerProcessor('flyback-queue', Queue);
