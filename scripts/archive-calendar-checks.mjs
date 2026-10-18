import assert from 'node:assert/strict';
import { ArchiveCalendar } from '../src/Mentor/wwwroot/archive-calendar.js';

function element(tag) {
  const attributes = new Map();
  const node = {
    tag, children: [], dataset: {}, className: '', textContent: '',
    append(...items) { this.children.push(...items); },
    prepend(...items) { this.children.unshift(...items); },
    replaceChildren(...items) { this.children = items; },
    setAttribute(name, value) { attributes.set(name, value); },
    getAttribute(name) { return attributes.get(name); },
    focus() { document.activeElement = this; },
    contains(target) { return this === target || this.children.some((child) => child.contains(target)); },
    querySelector(selector) {
      const [, attribute, value] = selector.match(/^\[([^=]+)="([^"]+)"\]$/);
      return descendants(this).find((child) =>
        (attribute === 'data-day' ? child.dataset.day : child.getAttribute(attribute)) === value) || null;
    },
  };
  node.classList = {
    add(name) { node.className += ` ${name}`; },
    contains(name) { return node.className.split(' ').includes(name); },
  };
  return node;
}
function descendants(node) {
  return node.children.flatMap((child) => [child, ...descendants(child)]);
}
globalThis.document = { createElement: element, activeElement: null };
const root = element('div');
const calendar = new ArchiveCalendar(root, (list, day) => list.append({ ...element('a'), textContent: day }));
const days = () => descendants(root).filter((node) => node.classList.contains('calendar-day'));
const list = () => descendants(root).find((node) => node.classList.contains('archive-conversations'));
const navigate = (label) => root.querySelector(`[aria-label="${label} měsíc"]`).onclick();
const analyses = [
  { day: '2024-02-29', status: 'completed' },
  { day: '2024-02-29', status: 'failed' },
  { day: '2024-02-28', status: 'running' },
  { day: '2024-02-27', status: 'queued' },
  { day: '2024-02-26', status: 'failed' },
];
calendar.update('2024-03-01', analyses);
assert.equal(days().length, 29, 'Leap February includes 29 days');
assert.deepEqual(days().filter((day) => day.tag === 'button').map((day) => day.dataset.day), ['2024-02-29']);
const grid = descendants(root).find((node) => node.className === 'calendar-grid');
assert.equal(grid.children.indexOf(days()[0]), 10, 'February starts on Thursday in a Monday-first calendar');
assert.equal(list().children.length, 0, 'No conversations before selection');
root.querySelector('[data-day="2024-02-29"]').onclick();
assert.equal(list().children[0].textContent, '2024-02-29');
assert.equal(document.activeElement.getAttribute('aria-pressed'), 'true');
calendar.update('2024-03-02', analyses);
assert.equal(list().children[0].textContent, '2024-02-29', 'Polling preserves selection');
assert.equal(document.activeElement, root.querySelector('[data-day="2024-02-29"]'), 'Polling restores focus');
navigate('Následující');
assert.equal(days().length, 31);
assert.equal(list().children.length, 0, 'Other months hide the selected day conversations');
navigate('Předchozí');
assert.equal(list().children[0].textContent, '2024-02-29');
calendar.update('2024-03-02', []);
assert.equal(list().children.length, 0, 'Removed analyses clear selection');
assert.equal(days().filter((day) => day.tag === 'button').length, 0);
calendar.update('2024-03-02', analyses);
navigate('Předchozí');
navigate('Předchozí');
assert.match(descendants(root).find((node) => node.id === 'archive-month').textContent, /2023/);
navigate('Následující');
assert.match(descendants(root).find((node) => node.id === 'archive-month').textContent, /2024/);
const empty = element('div');
new ArchiveCalendar(empty, () => assert.fail()).update('2026-10-18', []);
assert.equal(descendants(empty).filter((node) => node.getAttribute('aria-current') === 'date').length, 1);
assert.ok(descendants(empty).some((node) => node.className === 'empty'));
console.log('Archive calendar checks passed: completed days, leap years, Monday-first layout, selection, polling, focus, month/year navigation and empty state.');
