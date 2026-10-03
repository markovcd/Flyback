// Marks the page's sound as playback, which a phone treats as the thing to hear: other
// music stops, and the ringer switch no longer mutes it. Only browsers with an audio
// session (Safari) have one to set; elsewhere it does nothing.

export function takeAudio() {
  try {
    if (navigator.audioSession) navigator.audioSession.type = 'playback';
  } catch {
    // A browser that refuses the type plays as it would have.
  }
}

/**
 * Plays <node>'s sound through an <audio> element, which is what makes Android stop other
 * playback: Web Audio alone does not take audio focus. Returns the element, or null where
 * the node should go straight to the speakers (everywhere but Android).
 */
export function throughElement(context, node) {
  if (!/Android/i.test(navigator.userAgent)) return null;

  const stream = new MediaStreamAudioDestinationNode(context);
  node.connect(stream);

  const element = new Audio();
  element.srcObject = stream.stream;
  return element;
}
