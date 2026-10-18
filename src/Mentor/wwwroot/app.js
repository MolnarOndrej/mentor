import { api } from './api.js';
import { $, date, dayLabel, el, formatted } from './dom.js';
import { PromptEditor } from './prompt-editor.js';
import { analysisControl } from './analysis-control.js';
const labels = {
  queued: 'Ve frontě',
  importing: 'Načítání',
  ready: 'Uloženo',
  running: 'Probíhá rozbor',
  completed: 'Dokončeno',
  failed: 'Selhání',
};
function notice(text) {
  $('notice').textContent = text;
}
function button(text, action, cls = 'secondary') {
  const b = el('button', text, cls);
  b.onclick = async () => {
    b.disabled = true;
    try {
      await action();
      await refresh(true);
    } catch (e) {
      notice(e.message);
    } finally {
      b.disabled = false;
    }
  };
  return b;
}
function badge(status, text) {
  return el('span', text || labels[status] || status, 'badge ' + status);
}
let state,
  selectedDay = null,
  initialized = false,
  lastRender = '',
  refreshing = false;
let updatingConversations = false;
const promptEditor = new PromptEditor();
const reportCache = new Map();
let pollTimer, pollInterval;
function configurePolling(seconds) {
  if (seconds === pollInterval) return;
  clearInterval(pollTimer);
  pollInterval = seconds;
  pollTimer = setInterval(
    () => refresh().catch((e) => notice('Spojení s Mentorem selhalo: ' + e.message)),
    seconds * 1000
  );
}
function dayRows(day) {
  return state.conversations
    .filter((c) => c.days.some((d) => d.day === day))
    .map((c) => ({ ...c, coverage: c.days.find((d) => d.day === day) }));
}
function totals(day) {
  const rows = dayRows(day);
  return {
    conversations: rows.length,
    messages: rows.reduce((n, c) => n + c.coverage.messageCount, 0),
    analyzed: rows.reduce((n, c) => n + c.coverage.analyzedCount, 0),
  };
}
function canAnalyze(day) {
  return !updatingConversations && analysisControl(state, day).enabled;
}
function confirmDelete(c) {
  const dialog = $('delete-conversation-dialog');
  $('delete-conversation-description').textContent =
    `Opravdu chcete odstranit konverzaci „${c.title}“?`;
  dialog.returnValue = '';
  return new Promise((resolve) => {
    dialog.addEventListener('close', () => resolve(dialog.returnValue === 'delete'), {
      once: true,
    });
    dialog.showModal();
  });
}
function selectDay(day) {
  selectedDay = day;
  if ($('archive-dialog').open) $('archive-dialog').close();
  lastRender = '';
  refresh(true);
  $('report-heading').scrollIntoView({ behavior: 'smooth', block: 'start' });
}
function conversationCard(c, day, editable = false) {
  const card = el('div', undefined, 'card');
  const heading = el('div', undefined, 'conversation-heading');
  heading.append(button(`#${c.id} · ${c.title}`, () => showConversation(c.id), 'link-button'));
  if (editable) {
    const actions = el('div', undefined, 'actions conversation-actions');
    for (const [label, path, action] of [
      [
        'Aktualizovat konverzaci',
        'M20 4v6h-6 M20 10a8 8 0 1 0-2 8',
        async () => {
          updatingConversations = true;
          $('analyze').disabled = true;
          $('refresh-today').disabled = true;
          try {
            const result = await api(`conversations/${c.id}/refresh`, 'POST');
            notice(result.message);
          } finally {
            updatingConversations = false;
            await refresh(true);
          }
        },
      ],
      [
        'Smazat konverzaci',
        'M3 6h18 M9 6V3h6v3 M5 6l1 15h12l1-15 M10 10v7 M14 10v7',
        async () => {
          if (!(await confirmDelete(c))) return;
          await api(`conversations/${c.id}`, 'DELETE');
          notice('Konverzace byla smazána.');
        },
      ],
    ]) {
      const control = button('', action, 'secondary conversation-action');
      control.title = label;
      control.disabled = updatingConversations;
      control.setAttribute('aria-label', label);
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.setAttribute('viewBox', '0 0 24 24');
      svg.setAttribute('aria-hidden', 'true');
      const shape = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      shape.setAttribute('d', path);
      svg.append(shape);
      control.append(svg);
      actions.append(control);
    }
    heading.append(actions);
  }
  card.append(heading);
  const meta = el('div', undefined, 'meta');
  if (c.status !== 'ready') meta.append(badge(c.status));
  if (!day) meta.append(document.createTextNode(`Vloženo ${date(c.createdAt)}`));
  if (meta.childNodes.length) card.append(meta);
  if (c.error) card.append(el('p', c.error, 'error'));
  if (c.status === 'failed')
    card.append(button('Zkusit načíst znovu', () => api(`conversations/${c.id}/retry`, 'POST')));
  return card;
}
async function showPrompt(id) {
  const result = await api(`analyses/${id}/prompt`);
  $('prompt-title').textContent = `Odeslaný prompt · rozbor #${id}`;
  $('prompt-description').textContent = result.prompt
    ? 'Přesná uložená kopie vstupu tohoto běhu, včetně konverzací.'
    : 'Běh zatím čeká ve frontě. Úplný prompt se uloží při jeho zahájení.';
  $('full-prompt').value = result.prompt || '';
  $('prompt-dialog').showModal();
}
async function reportDetails(a) {
  if (a.status === 'completed' && reportCache.has(a.id)) return reportCache.get(a.id);
  const full = await api(`analyses/${a.id}`);
  if (a.status === 'completed') reportCache.set(a.id, full);
  return full;
}
async function fillAttempt(container, a) {
  const full = await reportDetails(a);
  container.replaceChildren();
  if (full.result) container.append(formatted(full.result));
  if (full.error) container.append(el('p', full.error, 'error'));
  const details = el('details');
  details.dataset.key = `report-details-${a.id}`;
  details.append(
    el('summary', 'Podrobnosti rozboru'),
    el(
      'p',
      `Verze #${a.id} · ${date(a.completedAt || a.createdAt)} · ${a.conversationIds.length} konverzací`,
      'meta'
    )
  );
  const tools = el('div', undefined, 'actions');
  tools.append(button('Odeslaný prompt', () => showPrompt(a.id), 'link-button'));
  for (const id of a.conversationIds)
    tools.append(button(`Konverzace #${id}`, () => showConversation(id), 'link-button'));
  details.append(tools);
  container.append(details);
}
async function renderCurrent(day) {
  const totalsForDay = totals(day);
  $('day').value = day;
  $('report-heading').textContent =
    day === state.today ? 'Dnešní rozbor' : `Rozbor · ${dayLabel(day)}`;
  $('report-date').textContent = day === state.today ? dayLabel(day) : '';
  $('back-today').hidden = day === state.today;
  const attempts = state.analyses.filter((a) => a.day === day).sort((a, b) => b.id - a.id),
    latest = attempts[0],
    success = attempts.find((a) => a.status === 'completed');
  const active = attempts.find((a) => ['queued', 'running'].includes(a.status));
  const control = analysisControl(state, day);
  $('analyze').disabled = !canAnalyze(day);
  $('refresh-today').hidden = day !== state.today;
  $('refresh-today').disabled = updatingConversations || !!active || !dayRows(day).length;
  $('refresh-today').setAttribute('aria-busy', String(updatingConversations));
  $('analyze').dataset.status = control.status;
  $('analyze').setAttribute('aria-label', control.label);
  $('analyze').title = control.label;
  $('analyze').setAttribute('aria-busy', String(!!active));
  const report = $('current-report');
  report.replaceChildren();
  if (active) {
    report.append(
      el(
        'p',
        active.status === 'queued' ? 'Čeká na dokončení importů.' : 'Připravuji denní rozbor…',
        'stat pending'
      ),
      button('Prompt tohoto běhu', () => showPrompt(active.id), 'link-button')
    );
  }
  if (latest?.status === 'failed') {
    const error = el('details');
    error.append(el('summary', 'Rozbor selhal · podrobnosti'), el('p', latest.error, 'error'));
    report.append(error);
  }
  if (success) {
    const content = el('div');
    report.append(content);
    await fillAttempt(content, success);
    if (state.changedDays.includes(day))
      report.append(el('p', 'Konverzace se změnily. Rozbor můžete aktualizovat.', 'hint'));
  } else if (!active && !totalsForDay.messages)
    report.append(el('p', 'Pro tento den zatím nejsou žádné zprávy.', 'empty'));
  $('day-conversations-heading').textContent = `Konverzace (${totalsForDay.conversations})`;
  $('day-conversations').hidden = !totalsForDay.conversations;
  const list = $('current-conversations');
  list.replaceChildren();
  for (const c of dayRows(day).sort((a, b) => b.id - a.id))
    list.append(conversationCard(c, day, day === state.today));
}
function renderArchive(days) {
  const archive = $('archive');
  archive.replaceChildren();
  $('archive-count').textContent = `${days.length} dní · ${state.conversations.length} konverzací`;
  for (const day of days) {
    const t = totals(day),
      pending = t.messages - t.analyzed;
    const group = el('details');
    group.dataset.key = `day-${day}`;
    const summary = el('summary');
    const line = el('span', undefined, 'day-summary');
    line.append(
      el('span', dayLabel(day)),
      el('span', `${t.conversations} konverzací`, 'subtle'),
      badge(
        pending ? 'queued' : 'completed',
        pending ? `${pending} zpráv čeká` : t.messages ? 'Analyzováno' : 'Bez zpráv'
      )
    );
    summary.append(line);
    group.append(summary);
    group.append(
      button('Otevřít denní rozbor', () => selectDay(day)),
      el('p', 'Konverzace', 'hint')
    );
    for (const c of dayRows(day).sort((a, b) => b.id - a.id))
      group.append(conversationCard(c, day));
    archive.append(group);
  }
  if (!days.length) archive.append(el('p', 'Zde budou přibývat jednotlivé dny.', 'empty'));
}
async function refresh(force = false) {
  if (refreshing) return;
  refreshing = true;
  try {
    const next = await api('state');
    if (!initialized) {
      promptEditor.initialize(next.settings.prompt);
      initialized = true;
    }
    state = next;
    configurePolling(state.settings.runtime.pollIntervalSeconds);
    $('open-runtime-settings').disabled = false;
    const days = [
      ...new Set([
        ...state.conversations.flatMap((c) => c.days.map((d) => d.day)),
        ...state.analyses.map((a) => a.day),
      ]),
    ]
      .sort()
      .reverse();
    const day = selectedDay || state.today;
    const signature = JSON.stringify([
      day,
      state.conversations,
      state.analyses,
      state.changedDays,
      updatingConversations,
    ]);
    if (signature === lastRender && !force) return;
    const open = new Set(
      [...document.querySelectorAll('details[data-key][open]')].map((e) => e.dataset.key)
    );
    await renderCurrent(day);
    renderArchive(days);
    for (const d of document.querySelectorAll('details[data-key]'))
      if (open.has(d.dataset.key)) d.open = true;
    const imports = $('import-status');
    imports.replaceChildren();
    const waitingDays = state.changedDays;
    $('archive-pending-count').textContent = waitingDays.length;
    $('archive-pending-count').hidden = !waitingDays.length;
    const archiveLabel = waitingDays.length
      ? `Archiv · ${waitingDays.length} dní čeká na rozbor`
      : 'Archiv · Starší dny a rozbory';
    $('open-archive').setAttribute('aria-label', archiveLabel);
    $('open-archive').title = archiveLabel;
    const loading = state.conversations.filter((c) =>
      ['queued', 'importing'].includes(c.status)
    ).length;
    if (loading) imports.append(el('p', `Načítám konverzace (${loading})…`, 'hint'));
    for (const c of state.conversations.filter((c) => c.status === 'failed')) {
      const error = el('details');
      error.append(el('summary', `Import selhal · ${c.title}`), conversationCard(c));
      imports.append(error);
    }
    lastRender = signature;
  } finally {
    refreshing = false;
  }
}
async function showConversation(id) {
  const c = await api('conversations/' + id);
  $('conversation-title').textContent = `#${c.id} · ${c.title}`;
  const content = $('conversation-content');
  content.replaceChildren();
  content.append(el('p', `Vloženo ${date(c.createdAt)}`, 'hint'));
  const messages = c.messages;
  if (!messages.length)
    content.append(el('p', c.error || 'Pro vybraný den nejsou dostupné zprávy.'));
  for (const m of messages) {
    const row = el('div', undefined, 'message');
    row.append(el('strong', `${m.number}. ${m.role === 'user' ? 'Vy' : 'AI'}`), el('pre', m.text));
    content.append(row);
  }
  if (!$('conversation-dialog').open) $('conversation-dialog').showModal();
}
$('close-conversation').onclick = () => $('conversation-dialog').close();
$('close-prompt').onclick = () => $('prompt-dialog').close();
$('open-settings').onclick = () => promptEditor.open($('day').value);
$('open-runtime-settings').onclick = () => {
  $('poll-interval').value = state.settings.runtime.pollIntervalSeconds;
  $('daily-time').value = state.settings.runtime.dailyTime;
  $('runtime-settings-error').textContent = '';
  $('runtime-settings-dialog').showModal();
};
$('close-runtime-settings').onclick = () => $('runtime-settings-dialog').close();
$('runtime-settings-form').onsubmit = async (e) => {
  e.preventDefault();
  e.submitter.disabled = true;
  $('runtime-settings-error').textContent = '';
  try {
    const saved = await api('settings', 'PUT', {
      pollIntervalSeconds: Number($('poll-interval').value),
      dailyTime: $('daily-time').value,
    });
    state.settings.runtime = saved;
    configurePolling(saved.pollIntervalSeconds);
    $('runtime-settings-dialog').close();
    notice('Nastavení bylo uloženo.');
  } catch (error) {
    $('runtime-settings-error').textContent = error.message;
  } finally {
    e.submitter.disabled = false;
  }
};
$('open-archive').onclick = () => $('archive-dialog').showModal();
$('close-archive').onclick = () => $('archive-dialog').close();
$('back-today').onclick = () => selectDay(state.today);
$('day').onchange = () => {
  if ($('day').value) selectDay($('day').value);
};
$('import-form').onsubmit = async (e) => {
  e.preventDefault();
  e.submitter.disabled = true;
  try {
    const result = await api('conversations', 'POST', { url: $('url').value });
    notice(result.added ? '' : 'Tento odkaz už máte uložený.');
    $('url').value = '';
    await refresh(true);
  } catch (e) {
    notice(e.message);
  } finally {
    e.submitter.disabled = false;
  }
};
$('analyze').onclick = async () => {
  const b = $('analyze');
  b.disabled = true;
  try {
    selectedDay = $('day').value;
    const input = await promptEditor.prepare(selectedDay);
    await api('analyses', 'POST', input);
    promptEditor.markSaved();
    notice('');
    await refresh(true);
  } catch (e) {
    notice(e.message);
  } finally {
    b.disabled = !canAnalyze(selectedDay || state?.today);
  }
};
$('refresh-today').onclick = async () => {
  updatingConversations = true;
  $('refresh-today').disabled = true;
  $('analyze').disabled = true;
  try {
    const result = await api('conversations/refresh-today', 'POST');
    const summary = `Zkontrolováno konverzací: ${result.checkedCount}. Aktualizováno: ${result.updatedCount}.`;
    notice([summary, ...new Set(result.messages)].join(' '));
  } catch (error) {
    notice(error.message);
  } finally {
    updatingConversations = false;
    await refresh(true);
  }
};
refresh().catch((e) => notice(e.message));
// Retry a failed initial connection until the database settings are available.
configurePolling(5);
