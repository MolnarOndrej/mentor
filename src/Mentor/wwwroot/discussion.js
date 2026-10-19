import { api } from './api.js';
import { el, formatted } from './dom.js';

export class DiscussionView {
  drafts = new Map();
  errors = new Map();
  submitting = false;

  constructor(changed, busy) {
    this.changed = changed;
    this.busy = busy;
  }

  async render(analysis, blocked) {
    const turns = await api(`analyses/${analysis.id}/discussion`);
    const section = el('section', undefined, 'discussion');
    section.setAttribute('aria-label', 'Diskuse k rozboru');
    section.append(el('h3', 'Diskuse k rozboru'));
    const pending = turns.some((t) => ['queued', 'running'].includes(t.status));
    const failed = turns.some((t) => t.status === 'failed');
    const disabled = blocked || pending || this.submitting;
    for (const turn of turns) {
      const row = el('div', undefined, 'discussion-turn');
      row.append(
        el('strong', `${turn.position}. Vy`),
        el('p', turn.question, 'discussion-question')
      );
      if (turn.answer) row.append(el('strong', 'Mentor'), formatted(turn.answer));
      if (['queued', 'running'].includes(turn.status)) {
        const progress = el(
          'p',
          turn.status === 'queued' ? 'Odpověď čeká ve frontě…' : 'Připravuji odpověď…',
          'stat pending'
        );
        progress.setAttribute('role', 'status');
        row.append(progress);
      }
      if (turn.error) row.append(el('p', turn.error, 'error'));
      if (turn.status === 'failed') {
        const retry = el('button', 'Zkusit odpověď znovu', 'secondary');
        retry.type = 'button';
        retry.disabled = disabled;
        retry.dataset.discussionControl = '';
        retry.onclick = () =>
          this.submit(analysis.id, section, () => api(`discussion/${turn.id}/retry`, 'POST'));
        row.append(retry);
      }
      section.append(row);
    }
    const form = el('form', undefined, 'discussion-form');
    const label = el('label', 'Otázka k rozboru');
    const input = el('textarea');
    input.id = `question-${analysis.id}`;
    label.htmlFor = input.id;
    input.rows = 4;
    input.maxLength = 240000;
    input.value = this.drafts.get(analysis.id) || '';
    input.disabled = disabled || failed;
    input.required = true;
    input.dataset.discussionControl = '';
    input.oninput = () => this.drafts.set(analysis.id, input.value);
    const send = el('button', 'Odeslat otázku');
    send.type = 'submit';
    send.disabled = disabled || failed;
    send.dataset.discussionControl = '';
    form.append(label, input, send);
    const error = el('p', this.errors.get(analysis.id) || '', 'error');
    error.setAttribute('role', 'alert');
    error.dataset.discussionError = '';
    form.onsubmit = (event) => {
      event.preventDefault();
      if (!input.value.trim()) return;
      this.submit(analysis.id, section, async () => {
        await api(`analyses/${analysis.id}/discussion`, 'POST', { question: input.value });
        this.drafts.delete(analysis.id);
      });
    };
    section.append(form, error);
    if (blocked) section.append(el('p', 'Během generování rozboru nelze odeslat otázku.', 'hint'));
    section.append(
      el(
        'p',
        'Kontext diskuse má limit 240 000 znaků. Při překročení se otázka neodešle; obsah se nezkracuje.',
        'hint'
      )
    );
    return section;
  }

  async submit(id, section, action) {
    if (this.submitting) return;
    this.submitting = true;
    this.errors.delete(id);
    section.querySelectorAll('[data-discussion-control]').forEach((control) => {
      control.disabled = true;
    });
    this.busy();
    try {
      await action();
    } catch (error) {
      this.errors.set(id, error.message);
      section.querySelector('[data-discussion-error]').textContent = error.message;
    } finally {
      this.submitting = false;
      await this.changed();
    }
  }
}
