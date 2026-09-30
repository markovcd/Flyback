// How a page judges its sound, for the web viewer's main.js and the web editor's speakers.js:
// left three seconds to settle, then counted over two at a time, and past twenty dropouts
// worked out a step lower. At 1× there is no lower step, and the page decides what then.

const SETTLING = 3000;
const JUDGED_OVER = 2000;
const DROPOUTS_ALLOWED = 20;

/** One sound's judge; `settle` starts it again, as a new play or a new rate does. */
export class Dropouts {
  #since = 0;
  #judgedAt = 0;
  #judgedStarved = 0;

  /** Starts the settling over from <now>, with <starved> dropouts so far. */
  settle(now, starved) {
    this.#since = now;
    this.#judgedAt = now;
    this.#judgedStarved = starved;
  }

  /**
   * Looks at the dropouts since the last look: 'lower' where the sound at <factor> should
   * be worked out a step lower, 'lowest' where it keeps dropping out at 1×, or null.
   */
  judge(now, starved, factor) {
    if (now - this.#since < SETTLING) {
      this.#judgedAt = now;
      this.#judgedStarved = starved;
      return null;
    }

    if (now - this.#judgedAt < JUDGED_OVER) return null;

    const dropped = starved - this.#judgedStarved;
    this.#judgedAt = now;
    this.#judgedStarved = starved;

    if (dropped <= DROPOUTS_ALLOWED) return null;
    if (factor <= 1) return 'lowest';

    this.settle(now, starved);
    return 'lower';
  }
}
