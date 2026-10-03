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
