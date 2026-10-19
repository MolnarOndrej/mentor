import assert from 'node:assert/strict';
import { DiscussionView } from '../src/Mentor/wwwroot/discussion.js';

function descendants(node) {
  return node.children.flatMap((child) => [child, ...descendants(child)]);
}
function element(tag) {
  return {
    tag, children: [], dataset: {}, attributes: {}, textContent: '',
    append(...children) { this.children.push(...children); },
    setAttribute(key, value) { this.attributes[key] = value; },
    querySelectorAll(selector) {
      const key = selector === '[data-discussion-control]' ? 'discussionControl' : 'discussionError';
      return descendants(this).filter((child) => key in child.dataset);
    },
    querySelector(selector) { return this.querySelectorAll(selector)[0]; },
  };
}
globalThis.document = {
  createElement: element,
  createTextNode: (text) => ({ ...element('#text'), textContent: text }),
};
const requests = [];
let turns = [], failure = null, release = null;
globalThis.fetch = async (url, options) => {
  requests.push({ url, ...options });
  if (options.method === 'POST' && release) await new Promise((resolve) => { release.resolve = resolve; });
  return {
    ok: !failure,
    status: failure ? 400 : 200,
    json: async () => ({ error: failure }),
    text: async () => options.method === 'GET' ? JSON.stringify(turns) : '{"id":3}',
  };
};
let changes = 0, busy = 0;
const view = new DiscussionView(async () => { changes++; }, () => { busy++; });
const report = { id: 7 };
const find = (section, tag) => descendants(section).find((node) => node.tag === tag);
let section = await view.render(report, false);
assert.equal(requests.length, 1);
assert.equal(requests[0].method, 'GET', 'Opening discussion only reads');
assert.equal(find(section, 'textarea').disabled, false);
find(section, 'textarea').value = '<script>draft</script>';
find(section, 'textarea').oninput();
section = await view.render(report, false);
assert.equal(find(section, 'textarea').value, '<script>draft</script>', 'Polling preserves drafts');
release = {};
const pending = view.submit(7, section, () => fetch('/test', { method: 'POST' }));
assert.equal(view.submitting, true);
assert.equal(busy, 1);
assert.ok(section.querySelectorAll('[data-discussion-control]').every((c) => c.disabled));
await view.submit(7, section, () => assert.fail('Duplicate submission'));
release.resolve();
await pending;
release = null;
assert.equal(changes, 1);
assert.equal(view.submitting, false);
await view.submit(7, section, () => { throw new Error('Context too large'); });
section = await view.render(report, false);
assert.equal(section.querySelector('[data-discussion-error]').textContent, 'Context too large');
assert.equal(find(section, 'textarea').value, '<script>draft</script>');
turns = [{ id: 3, position: 1, question: '<img onerror=alert(1)>', answer: '**Saved answer**', status: 'completed' }];
section = await view.render(report, true);
assert.ok(descendants(section).some((n) => n.textContent === '<img onerror=alert(1)>'));
assert.ok(descendants(section).some((n) => n.textContent === 'Saved answer'));
assert.ok(section.querySelectorAll('[data-discussion-control]').every((c) => c.disabled), 'Regeneration preserves readable history and blocks controls');
for (const status of ['queued', 'running']) {
  turns[0].status = status;
  section = await view.render(report, false);
  assert.equal(find(section, 'textarea').disabled, true);
  assert.ok(descendants(section).some((n) => n.attributes.role === 'status'), 'Progress is visible');
}
turns[0].status = 'failed';
turns[0].error = 'CLI failed';
section = await view.render(report, false);
const retry = find(section, 'button');
assert.equal(retry.disabled, false);
assert.equal(find(section, 'textarea').disabled, true);
await retry.onclick();
assert.equal(requests.at(-1).url, '/api/discussion/3/retry');
assert.equal(requests.at(-1).method, 'POST');
section = await view.render(report, true);
assert.ok(section.querySelectorAll('[data-discussion-control]').every((c) => c.disabled));
console.log('Discussion checks passed: read-only opening, drafts, submission, errors, progress, safe history, retry and regeneration locks.');
