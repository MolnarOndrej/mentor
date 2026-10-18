import { api } from './api.js';
import { $ } from './dom.js';
import { AutoSave } from './auto-save.js';

export class PromptEditor {
  #data = null;
  #day = null;
  #request = 0;
  #save;

  constructor() {
    this.#save = new AutoSave(
      () => ({ prompt: $('prompt').value, data: this.#data }),
      async (value) => {
        if (!$('prompt').reportValidity()) throw Error('Vyplňte instrukce kouče.');
        await api('prompt', 'PUT', value);
      },
      (message) => {
        $('prompt-error').textContent = message;
      }
    );
    $('prompt').oninput = () => this.#save.schedule();
    $('prompt').onblur = () => this.#save.save();
    $('prompt-form').onsubmit = (event) => {
      event.preventDefault();
      this.#save.save();
    };
  }

  initialize(template) {
    $('prompt').value = template;
    this.#save.markSaved();
    $('prompt').disabled = false;
  }

  open(day) {
    if (this.#day === day) return;
    this.#load(day).catch(() => {}); // The editor shows the actual error; keep the draft.
  }

  async prepare(day) {
    await this.#load(day);
    return { day, prompt: $('prompt').value, data: this.#data };
  }

  async analyze(day) {
    const input = await this.prepare(day);
    $('prompt').disabled = true;
    try {
      await api('analyses', 'POST', input);
    } finally {
      $('prompt').disabled = false;
    }
  }

  #template() {
    const text = $('prompt').value;
    if (this.#data) {
      const index = text.indexOf(this.#data);
      if (index >= 0)
        return text.slice(0, index) + '{{data}}' + text.slice(index + this.#data.length);
    }
    return text;
  }

  async #load(day) {
    const request = ++this.#request;
    $('prompt').disabled = true;
    try {
      await this.#save.flush();
      if (request !== this.#request) return;
      const result = await api('prompt/render', 'POST', { day, prompt: this.#template() });
      if (request !== this.#request) return;
      $('prompt').value = result.prompt;
      this.#data = result.data;
      this.#day = day;
      this.#save.markSaved();
      $('prompt-error').textContent = '';
    } catch (error) {
      if (request === this.#request) $('prompt-error').textContent = error.message;
      throw error;
    } finally {
      if (request === this.#request) $('prompt').disabled = false;
    }
  }
}
