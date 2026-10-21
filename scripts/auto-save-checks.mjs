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

// Exercise the actual editors without a browser, including validation and automatic data slots.
const elements = new Map();
for (const id of [
  'prompt',
  'prompt-form',
  'prompt-error',
  'settings-form',
  'poll-interval',
  'daily-time',
  'settings-error',
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
  assert.notEqual(path, '/api/prompt/render');
  const result = path === '/api/settings' ? body : { template: body.prompt };
  return { ok: true, text: async () => JSON.stringify(result) };
};
const { PromptEditor } = await import('../src/Mentor/wwwroot/prompt-editor.js');
const editor = new PromptEditor();
editor.initialize('Coach\n\nVSTUPNÍ DATA (JSON):\r\n{{data}}\r\n');
assert.equal(elements.get('prompt').value, 'Coach');
assert.equal(
  elements.get('prompt').maxLength,
  20_000 - '\n\nVSTUPNÍ DATA (JSON):\n{{data}}'.length
);
elements.get('prompt').value = 'Changed';
elements.get('prompt').oninput();
await editor.analyze('2026-10-18');
assert.deepEqual(
  requests.map((request) => request.path),
  ['/api/prompt', '/api/analyses']
);
assert.deepEqual(requests.at(-1).body, {
  day: '2026-10-18',
  prompt: 'Changed\n\nVSTUPNÍ DATA (JSON):\n{{data}}',
});
assert.equal(elements.get('prompt').value, 'Changed');
assert.equal(timers.size, 0);
elements.get('prompt').value = 'New instructions';
elements.get('prompt').oninput();
await tick();
assert.deepEqual(requests.at(-1).body, {
  prompt: 'New instructions\n\nVSTUPNÍ DATA (JSON):\n{{data}}',
});
await editor.analyze('2026-10-18');
assert.equal(requests.at(-1).path, '/api/analyses');
assert.equal(elements.get('prompt').disabled, false);
// All editable text survives even when the stored data slot was inside the template.
editor.initialize('Before\n{{data}}\nAfter');
assert.equal(elements.get('prompt').value, 'Before\n\nAfter');

const { SettingsEditor } = await import('../src/Mentor/wwwroot/settings-editor.js');
let settings;
const settingsEditor = new SettingsEditor((saved) => {
  settings = saved;
});
settingsEditor.initialize({ pollIntervalSeconds: 5, dailyTime: '22:00' });
const form = elements.get('settings-form');
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
assert.match(elements.get('settings-error').textContent, /platný interval/);
form.valid = true;
elements.get('daily-time').value = '21:30';
await form.focusout();
assert.equal(settings.dailyTime, '21:30');
assert.equal(elements.get('settings-error').textContent, '');
console.log('Editor integration checks passed.');
