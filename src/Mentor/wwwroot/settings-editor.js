import { api } from './api.js';
import { $ } from './dom.js';
import { AutoSave } from './auto-save.js';

export class SettingsEditor {
  #save;

  constructor(onSaved) {
    const form = $('settings-form');
    this.#save = new AutoSave(
      () => ({
        pollIntervalSeconds: Number($('poll-interval').value),
        dailyTime: $('daily-time').value,
      }),
      async (value) => {
        if (!form.reportValidity()) throw Error('Vyplňte platný interval a čas.');
        onSaved(await api('settings', 'PUT', value));
      },
      (message) => {
        $('settings-error').textContent = message;
      }
    );
    form.oninput = () => this.#save.schedule();
    form.addEventListener('focusout', () => this.#save.save());
    form.onsubmit = (event) => {
      event.preventDefault();
      this.#save.save();
    };
  }

  initialize(settings) {
    $('poll-interval').value = settings.pollIntervalSeconds;
    $('daily-time').value = settings.dailyTime;
    $('poll-interval').disabled = false;
    $('daily-time').disabled = false;
    this.#save.markSaved();
  }
}
