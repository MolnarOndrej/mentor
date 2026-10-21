export class Navigation {
  #page = null;
  #scroll = new Map();

  constructor() {
    for (const link of document.querySelectorAll('nav a[data-page], #home-link')) {
      link.addEventListener('click', (event) => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey)
          return;
        event.preventDefault();
        this.go(link.dataset.page || 'overview');
      });
    }
    window.addEventListener('popstate', () => this.#render());
    window.addEventListener('hashchange', () => this.#render());
    history.scrollRestoration = 'manual';
  }

  start() {
    this.#render();
  }

  go(page) {
    if (page === this.#page) return;
    history.pushState(null, '', `#/${page}`);
    this.#render();
  }

  #render() {
    const requested = location.hash.slice(2);
    const page = ['overview', 'settings', 'prompt', 'archive'].includes(requested)
      ? requested
      : 'overview';
    if (location.hash !== `#/${page}`) history.replaceState(null, '', `#/${page}`);
    if (page === this.#page) return;
    if (this.#page) this.#scroll.set(this.#page, window.scrollY);
    this.#page = page;
    for (const view of document.querySelectorAll('[data-view]'))
      view.hidden = view.dataset.view !== page;
    for (const link of document.querySelectorAll('nav a[data-page]')) {
      if (link.dataset.page === page) link.setAttribute('aria-current', 'page');
      else link.removeAttribute('aria-current');
    }
    const title = document.querySelector(`nav [data-page="${page}"] span:last-child`).textContent;
    document.title = `Mentor · ${title}`;
    const heading = document.querySelector(`[data-view="${page}"] h2`);
    heading.tabIndex = -1;
    heading.focus({ preventScroll: true });
    window.scrollTo(0, this.#scroll.get(page) || 0);
  }
}
