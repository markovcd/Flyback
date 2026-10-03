// A tap on an element: a quick press that barely moves. The release is read on the page,
// not the element, since the web editor's Avalonia takes the pointer on a press and
// no click then reaches the element. A touch's release is what lets a page start a sound.

export function onTap(element, tapped) {
  let pressed = null;

  element.addEventListener('pointerdown', event => {
    pressed = event.isPrimary && event.button === 0
      ? { id: event.pointerId, x: event.clientX, y: event.clientY, at: event.timeStamp }
      : null;
  });

  addEventListener('pointerup', event => {
    const down = pressed;
    pressed = null;

    if (down !== null && event.pointerId === down.id && event.timeStamp - down.at < 500
      && Math.hypot(event.clientX - down.x, event.clientY - down.y) < 10) tapped();
  }, { capture: true });

  addEventListener('pointercancel', () => { pressed = null; }, { capture: true });
}
