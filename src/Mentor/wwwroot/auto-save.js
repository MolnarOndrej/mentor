export class AutoSave {
  #read;
  #write;
  #error;
  #saved;
  #timer;
  #pending = null;

  constructor(read, write, error) {
    this.#read = read;
    this.#write = write;
    this.#error = error;
    window.addEventListener('beforeunload', (event) => {
      if (!this.dirty) return;
      event.preventDefault();
      event.returnValue = '';
    });
  }

  get dirty() {
    return (
      this.#pending !== null ||
      (this.#saved !== undefined && JSON.stringify(this.#read()) !== this.#saved)
    );
  }

  markSaved() {
    this.#saved = JSON.stringify(this.#read());
  }

  schedule() {
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => this.save(), 1000);
  }

  save() {
    return this.flush().catch(() => {}); // The field's error remains visible.
  }

  async flush() {
    clearTimeout(this.#timer);
    while (this.#pending) await this.#pending;
    if (!this.dirty) return;
    const value = this.#read();
    const snapshot = JSON.stringify(value);
    this.#pending = this.#write(value);
    try {
      await this.#pending;
      this.#saved = snapshot;
      this.#error('');
    } catch (error) {
      this.#error('Změny se nepodařilo uložit: ' + error.message);
      throw error;
    } finally {
      this.#pending = null;
    }
  }
}
