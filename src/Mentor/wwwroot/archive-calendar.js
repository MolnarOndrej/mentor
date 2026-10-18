import { dayLabel, el } from './dom.js';

export class ArchiveCalendar {
  #container;
  #renderConversations;
  #days = new Set();
  #month;
  #selectedDay = null;
  #today;

  constructor(container, renderConversations) {
    this.#container = container;
    this.#renderConversations = renderConversations;
  }

  update(today, analyses) {
    const focused = this.#container.contains(document.activeElement)
      ? document.activeElement
      : null;
    this.#today = today;
    this.#days = new Set(analyses.filter((a) => a.status === 'completed').map((a) => a.day));
    this.#month ??= ([...this.#days].sort().at(-1) || today).slice(0, 7);
    if (!this.#days.has(this.#selectedDay)) this.#selectedDay = null;
    this.#render();
    if (focused?.dataset.day)
      this.#container.querySelector(`[data-day="${focused.dataset.day}"]`)?.focus();
    else if (focused?.classList.contains('calendar-nav'))
      this.#container
        .querySelector(`[aria-label="${focused.getAttribute('aria-label')}"]`)
        ?.focus();
  }

  #moveMonth(offset) {
    const first = new Date(`${this.#month}-01T12:00:00`);
    first.setMonth(first.getMonth() + offset);
    this.#month = `${first.getFullYear()}-${String(first.getMonth() + 1).padStart(2, '0')}`;
    this.#render();
  }

  #render() {
    const calendar = el('div', undefined, 'archive-calendar');
    const header = el('div', undefined, 'calendar-heading');
    const month = el(
      'h3',
      new Intl.DateTimeFormat('cs-CZ', {
        month: 'long',
        year: 'numeric',
      }).format(new Date(`${this.#month}-01T12:00:00`))
    );
    month.id = 'archive-month';
    month.setAttribute('aria-live', 'polite');
    for (const [offset, label, path] of [
      [-1, 'Předchozí měsíc', 'm15 6-6 6 6 6'],
      [1, 'Následující měsíc', 'm9 6 6 6-6 6'],
    ]) {
      const control = el('button', undefined, 'secondary calendar-nav');
      control.setAttribute('aria-label', label);
      const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      icon.setAttribute('viewBox', '0 0 24 24');
      icon.setAttribute('aria-hidden', 'true');
      const shape = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      shape.setAttribute('d', path);
      icon.append(shape);
      control.append(icon);
      control.onclick = () => {
        this.#moveMonth(offset);
        this.#container.querySelector(`[aria-label="${label}"]`).focus();
      };
      header.append(control);
    }
    header.prepend(month);
    calendar.append(header);
    const grid = el('div', undefined, 'calendar-grid');
    grid.setAttribute('role', 'group');
    grid.setAttribute('aria-labelledby', month.id);
    for (const weekday of ['Po', 'Út', 'St', 'Čt', 'Pá', 'So', 'Ne'])
      grid.append(el('span', weekday, 'calendar-weekday'));
    const first = new Date(`${this.#month}-01T12:00:00`);
    const offset = (first.getDay() + 6) % 7;
    for (let i = 0; i < offset; i++) grid.append(el('span'));
    const count = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate();
    for (let number = 1; number <= count; number++) {
      const day = `${this.#month}-${String(number).padStart(2, '0')}`;
      const available = this.#days.has(day);
      const cell = el(available ? 'button' : 'span', String(number), 'calendar-day');
      cell.setAttribute(
        'aria-label',
        `${dayLabel(day)} · ${available ? 'Dokončený rozbor' : 'Bez rozboru'}`
      );
      if (day === this.#today) cell.setAttribute('aria-current', 'date');
      if (available) {
        cell.classList.add('has-analysis');
        cell.dataset.day = day;
        cell.setAttribute('aria-pressed', String(day === this.#selectedDay));
        cell.onclick = () => {
          this.#selectedDay = day;
          this.#render();
          this.#container.querySelector(`[data-day="${day}"]`).focus();
        };
      }
      grid.append(cell);
    }
    calendar.append(grid);
    this.#container.replaceChildren(calendar);
    if (!this.#days.size)
      this.#container.append(el('p', 'Zatím tu nejsou žádné dokončené rozbory.', 'empty'));
    const list = el('div', undefined, 'archive-conversations');
    list.setAttribute('aria-live', 'polite');
    if (this.#selectedDay?.startsWith(this.#month)) {
      list.setAttribute('aria-label', dayLabel(this.#selectedDay));
      this.#renderConversations(list, this.#selectedDay);
    }
    this.#container.append(list);
  }
}
