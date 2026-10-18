import { api } from './api.js';
import { $ } from './dom.js';
import { AutoSave } from './auto-save.js';

const dataSlot = '\n\nVSTUPNÍ DATA (JSON):\n{{data}}';

export class PromptEditor {
  #save;

  constructor() {
    this.#save = new AutoSave(
      () => $('prompt').value,
      async (instructions) => {
        if (!$('prompt').reportValidity()) throw Error('Vyplňte instrukce kouče.');
        await api('prompt', 'PUT', { prompt: this.#template(instructions) });
      },
      (message) => {
        $('prompt-error').textContent = message;
      }
    );
    $('prompt').maxLength = 20_000 - dataSlot.length;
    $('prompt').oninput = () => this.#save.schedule();
    $('prompt').onblur = () => this.#save.save();
    $('prompt-form').onsubmit = (event) => {
      event.preventDefault();
      this.#save.save();
    };
  }

  initialize(template) {
    $('prompt').value = template
      .replace(/VSTUPNÍ DATA \(JSON\):\s*\{\{data\}\}/, '')
      .replace('{{data}}', '')
      .trim();
    this.#save.markSaved();
    $('prompt').disabled = false;
  }

  async analyze(day) {
    $('prompt').disabled = true;
    try {
      await this.#save.flush();
      await api('analyses', 'POST', { day, prompt: this.#template($('prompt').value) });
    } finally {
      $('prompt').disabled = false;
    }
  }

  #template(instructions) {
    return instructions.trimEnd() + dataSlot;
  }
}
