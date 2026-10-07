// The tutorials page: one player, the playlist beside it, and the address naming the
// video playing, so a link to #your-first-patch opens that one.
(() => {
  const entries = [...document.querySelectorAll('.playlist a[data-video]')];
  const frame = document.querySelector('.player iframe');
  if (!entries.length || !frame) return;

  const count = document.querySelector('.now .count');
  const title = document.querySelector('.now h2');
  const blurb = document.querySelector('.now p');
  const [previous, next] = document.querySelectorAll('.steps button');
  let at = -1;

  const named = () => Math.max(0, entries.findIndex(e => '#' + e.dataset.slug === location.hash));

  function show(i, play) {
    if (i === at || i < 0 || i >= entries.length) return;
    at = i;
    const e = entries[i];
    const name = e.querySelector('strong').textContent;
    // The page is written showing the first video; it is not loaded a second time.
    if (play || !frame.src.includes(e.dataset.video)) frame.src = `https://www.youtube-nocookie.com/embed/${e.dataset.video}${play ? '?autoplay=1' : ''}`;
    frame.title = name;
    count.textContent = `${i + 1} of ${entries.length}`;
    title.textContent = name;
    blurb.textContent = e.dataset.blurb;
    entries.forEach((other, j) => other.setAttribute('aria-current', String(j === i)));
    previous.disabled = i === 0;
    next.disabled = i === entries.length - 1;
  }

  function go(i) {
    if (i < 0 || i >= entries.length) return;
    history.pushState(null, '', '#' + entries[i].dataset.slug);
    show(i, true);
    if (frame.getBoundingClientRect().top < 0) frame.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  entries.forEach((e, i) => e.addEventListener('click', ev => {
    if (ev.ctrlKey || ev.metaKey || ev.shiftKey || ev.button !== 0) return;
    ev.preventDefault();
    go(i);
  }));
  previous.addEventListener('click', () => go(at - 1));
  next.addEventListener('click', () => go(at + 1));
  addEventListener('popstate', () => show(named(), false));
  show(named(), false);
})();
