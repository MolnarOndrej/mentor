import assert from 'node:assert/strict';
import { AutoSave } from '../src/Mentor/wwwroot/auto-save.js';

const listeners = [];
globalThis.window = { addEventListener: (name, callback) => listeners.push(callback) };
const timers = new Map();
let timerId = 0;
globalThis.setTimeout = (callback, delay) => {
  assert.equal(delay, 1000);
  timers.set(++timerId, callback);
  return timerId;
};
globalThis.clearTimeout = (id) => timers.delete(id);
const tick = async () => {
  const callbacks = [...timers.values()];
  timers.clear();
  await Promise.all(callbacks.map((callback) => callback()));
};

let value = 'original';
const writes = [];
let error = '';
let fail = false;
let release;
let hold = false;
const save = new AutoSave(
  () => ({ value }),
  async (snapshot) => {
    writes.push(snapshot.value);
    if (hold)
      await new Promise((resolve) => {
        release = resolve;
      });
    if (fail) throw Error('offline');
  },
  (message) => {
    error = message;
  }
);
save.markSaved();
await save.flush();
assert.deepEqual(writes, []);
value = 'first';
save.schedule();
value = 'second';
save.schedule();
assert.equal(timers.size, 1);
await tick();
assert.deepEqual(writes, ['second']);
assert.equal(save.dirty, false);

// A blur flushes immediately and cancels the pending debounce.
value = 'blur';
save.schedule();
await save.flush();
assert.equal(timers.size, 0);
assert.equal(writes.at(-1), 'blur');

// Slow requests are serialized; their responses cannot mark newer edits as saved.
hold = true;
value = 'slow';
const first = save.flush();
value = 'latest';
const second = save.flush();
const third = save.flush();
assert.equal(writes.at(-1), 'slow');
hold = false;
release();
await Promise.all([first, second, third]);
assert.deepEqual(writes.slice(-2), ['slow', 'latest']);
assert.equal(save.dirty, false);

fail = true;
value = 'retry';
await save.save();
assert.match(error, /offline/);
assert.equal(save.dirty, true);
let prevented = false;
listeners[0]({
  preventDefault: () => {
    prevented = true;
  },
});
assert.equal(prevented, true);
fail = false;
await save.flush();
assert.equal(error, '');
assert.equal(save.dirty, false);
console.log('Autosave checks passed.');

// Exercise the actual editors without a browser, including validation and rendering data.
const elements = new Map();
for (const id of [
  'prompt',
  'prompt-form',
  'prompt-error',
  'runtime-settings-form',
  'poll-interval',
  'daily-time',
  'runtime-settings-error',
]) {
  elements.set(id, {
    value: '',
    disabled: true,
    textContent: '',
    valid: true,
    reportValidity() {
      return this.valid;
    },
    addEventListener(name, callback) {
      this[name] = callback;
    },
  });
}
globalThis.document = { getElementById: (id) => elements.get(id) };
const requests = [];
globalThis.fetch = async (path, options) => {
  if (path === '/api/analyses') assert.equal(elements.get('prompt').disabled, true);
  const body = JSON.parse(options.body);
  requests.push({ path, body });
  const result =
    path === '/api/prompt/render'
      ? { prompt: body.prompt.replace('{{data}}', 'conversation'), data: 'conversation' }
      : path === '/api/settings'
        ? body
        : { template: body.prompt };
  return { ok: true, text: async () => JSON.stringify(result) };
};
const { PromptEditor } = await import('../src/Mentor/wwwroot/prompt-editor.js');
const editor = new PromptEditor();
editor.initialize('Coach {{data}}');
elements.get('prompt').value = 'Changed {{data}}';
elements.get('prompt').oninput();
const prepared = await editor.prepare('2026-10-18');
assert.deepEqual(
  requests.map((request) => request.path),
  ['/api/prompt', '/api/prompt/render']
);
assert.equal(prepared.prompt, 'Changed conversation');
assert.equal(timers.size, 0);
elements.get('prompt').value = 'New instructions conversation';
elements.get('prompt').oninput();
await tick();
assert.deepEqual(requests.at(-1).body, {
  prompt: 'New instructions conversation',
  data: 'conversation',
});
await editor.analyze('2026-10-18');
assert.equal(requests.at(-1).path, '/api/analyses');
assert.equal(elements.get('prompt').disabled, false);

const { RuntimeSettingsEditor } = await import('../src/Mentor/wwwroot/runtime-settings-editor.js');
let settings;
const settingsEditor = new RuntimeSettingsEditor((saved) => {
  settings = saved;
});
settingsEditor.initialize({ pollIntervalSeconds: 5, dailyTime: '22:00' });
const form = elements.get('runtime-settings-form');
elements.get('poll-interval').value = '10';
form.oninput();
await tick();
assert.deepEqual(settings, { pollIntervalSeconds: 10, dailyTime: '22:00' });
form.valid = false;
elements.get('daily-time').value = '';
const count = requests.length;
form.oninput();
await tick();
assert.equal(requests.length, count);
assert.match(elements.get('runtime-settings-error').textContent, /platný interval/);
form.valid = true;
elements.get('daily-time').value = '21:30';
await form.focusout();
assert.equal(settings.dailyTime, '21:30');
assert.equal(elements.get('runtime-settings-error').textContent, '');
console.log('Editor integration checks passed.');
