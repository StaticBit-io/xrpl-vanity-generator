// Measures the lit regions of the first-run tutorial and reports what the bubble needs to follow
// them. Regions are found by their data-tutorial attribute (see TutorialRegion.razor).

let stop = null;

/**
 * Returns the rectangle of each named region that is on the page, in the order given, the
 * viewport and the bubble's own size. Each rectangle is kept apart because the union of several
 * regions can span the window and hide the unlit space that is free for the bubble. With reveal,
 * the first region is scrolled into view first, so a step on a small window never points at
 * something below the fold; the bubble follows a user's own scrolling without it.
 */
export function measure(names, reveal = true) {
  const elements = [];
  for (const name of names ?? []) {
    const element = document.querySelector(`[data-tutorial="${name}"]`);
    if (element) {
      elements.push(element);
    }
  }

  if (reveal && elements.length > 0) {
    elements[0].scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }

  const targets = [];
  for (const element of elements) {
    const rect = element.getBoundingClientRect();
    if (rect.width > 0 && rect.height > 0) {
      targets.push({ left: rect.left, top: rect.top, width: rect.width, height: rect.height });
    }
  }

  const bubble = document.querySelector('[data-tutorial-bubble]');
  const bubbleRect = bubble ? bubble.getBoundingClientRect() : null;
  return {
    targets,
    viewWidth: document.documentElement.clientWidth,
    viewHeight: document.documentElement.clientHeight,
    bubbleWidth: bubbleRect ? bubbleRect.width : 0,
    bubbleHeight: bubbleRect ? bubbleRect.height : 0,
  };
}

/**
 * Reports resizes, scrolling and page changes to OnViewChanged, and Escape to OnEscape. Page
 * changes cover the move to another route and regions that render late. Escape inside a dialog
 * stays with that dialog.
 */
export function watch(dotNetRef) {
  unwatch();

  let timer = 0;
  const report = () => {
    clearTimeout(timer);
    // Live statistics change the page every second; one measurement per burst is enough.
    timer = setTimeout(() => dotNetRef.invokeMethodAsync('OnViewChanged').catch(() => { }), 100);
  };
  const onKeyDown = (event) => {
    if (event.key !== 'Escape' || event.defaultPrevented) {
      return;
    }

    if (event.target instanceof Element && event.target.closest('.modal-box')) {
      return;
    }

    dotNetRef.invokeMethodAsync('OnEscape').catch(() => { });
  };
  const observer = new MutationObserver((mutations) => {
    const bubble = document.querySelector('[data-tutorial-bubble]');
    if (mutations.some((mutation) => !bubble || !bubble.contains(mutation.target))) {
      report();
    }
  });

  window.addEventListener('resize', report);
  document.addEventListener('scroll', report, true);
  document.addEventListener('keydown', onKeyDown);
  observer.observe(document.body, { childList: true, subtree: true });

  stop = () => {
    clearTimeout(timer);
    window.removeEventListener('resize', report);
    document.removeEventListener('scroll', report, true);
    document.removeEventListener('keydown', onKeyDown);
    observer.disconnect();
  };
}

export function unwatch() {
  if (stop) {
    stop();
    stop = null;
  }
}
