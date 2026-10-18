import { api } from './api.js';
import { $ } from './dom.js';

export class PromptEditor {
  #saved = '';
  #data = null;
  #day = null;
  #request = 0;

  constructor() {
    $('prompt').oninput = () => {
      $('prompt-status').textContent =
        $('prompt').value === this.#saved
          ? 'Zadání je uložené.'
          : 'Neuložené úpravy · uloží se také při spuštění analýzy.';
    };
    $('close-settings').onclick = () => $('settings-dialog').close();
    $('prompt-form').onsubmit = async (event) => {
      event.preventDefault();
      try {
        await api('prompt', 'PUT', { prompt: $('prompt').value, data: this.#data });
        this.markSaved();
      } catch (error) {
        $('prompt-status').textContent = error.message;
      }
    };
  }

  initialize(template) {
    $('prompt').value = template;
    this.#saved = template;
  }

  open(day) {
    $('settings-dialog').showModal();
    this.#load(day).catch(() => {}); // The dialog shows the actual error; keep the draft.
  }

  markSaved() {
    this.#saved = $('prompt').value;
    $('prompt-status').textContent = 'Zadání je uložené pro příští rozbory.';
  }

  async prepare(day) {
    if (this.#data && this.#day !== day) await this.#load(day);
    return { day, prompt: $('prompt').value, data: this.#data };
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
    const submit = $('prompt-form').querySelector('button[type="submit"]');
    $('prompt').disabled = true;
    submit.disabled = true;
    $('prompt-status').textContent = 'Načítám celý prompt…';
    try {
      const dirty = $('prompt').value !== this.#saved;
      const result = await api('prompt/render', 'POST', { day, prompt: this.#template() });
      if (request !== this.#request) return;
      $('prompt').value = result.prompt;
      this.#data = result.data;
      this.#day = day;
      if (!dirty) this.#saved = result.prompt;
      $('prompt-status').textContent = dirty ? 'Neuložené úpravy.' : '';
    } catch (error) {
      if (request === this.#request) $('prompt-status').textContent = error.message;
      throw error;
    } finally {
      if (request === this.#request) {
        $('prompt').disabled = false;
        submit.disabled = false;
      }
    }
  }
}
