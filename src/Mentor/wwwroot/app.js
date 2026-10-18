import { api } from './api.js';
import { $, date, dayLabel, el, formatted } from './dom.js';
import { PromptEditor } from './prompt-editor.js';
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
const promptEditor = new PromptEditor();
const reportCache = new Map();
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
  return (
    !!state &&
    dayRows(day).length > 0 &&
    !state.analyses.some((a) => a.day === day && ['queued', 'running'].includes(a.status))
  );
}
function selectDay(day) {
  selectedDay = day;
  if ($('archive-dialog').open) $('archive-dialog').close();
  lastRender = '';
  refresh(true);
  $('report-heading').scrollIntoView({ behavior: 'smooth', block: 'start' });
}
function conversationCard(c, day) {
  const card = el('div', undefined, 'card');
  card.append(button(`#${c.id} · ${c.title}`, () => showConversation(c.id, day), 'link-button'));
  const meta = el('div', undefined, 'meta');
  if (c.status !== 'ready') meta.append(badge(c.status));
  if (day) {
    const d = c.days.find((d) => d.day === day);
    const active = state.analyses.find(
      (a) => a.day === day && ['queued', 'running'].includes(a.status)
    );
    meta.append(
      badge(
        active?.status || 'ready',
        active
          ? labels[active.status]
          : d.analyzedCount === d.messageCount
            ? 'Analyzováno'
            : 'Čeká na denní rozbor'
      ),
      document.createTextNode(
        `${d.analyzedCount}/${d.messageCount} zpráv zahrnuto v úspěšném rozboru`
      )
    );
  } else
    meta.append(
      document.createTextNode(`Vloženo ${date(c.createdAt)} · ${c.messageCount} uložených zpráv`)
    );
  card.append(meta);
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
function attemptSummary(a) {
  return `#${a.id} · ${labels[a.status]} · ${date(a.createdAt)} · ${a.conversationIds.length} konverzací / ${a.messageCount} zpráv`;
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
    tools.append(button(`Konverzace #${id}`, () => showConversation(id, a.day), 'link-button'));
  details.append(tools);
  container.append(details);
}
function attemptsList(attempts) {
  const box = el('div');
  for (const a of attempts) {
    const details = el('details');
    details.dataset.key = `attempt-${a.id}`;
    details.append(el('summary', attemptSummary(a)));
    const content = el('div');
    details.append(content);
    let loaded = false;
    details.ontoggle = () => {
      if (details.open && !loaded) {
        loaded = true;
        fillAttempt(content, a).catch((e) => {
          loaded = false;
          content.replaceChildren(el('p', e.message, 'error'));
        });
      }
    };
    box.append(details);
  }
  return box;
}
async function renderCurrent(day) {
  const totalsForDay = totals(day),
    pending = totalsForDay.messages - totalsForDay.analyzed;
  $('day').value = day;
  $('report-heading').textContent =
    day === state.today ? 'Dnešní rozbor' : `Rozbor · ${dayLabel(day)}`;
  $('report-date').textContent = day === state.today ? dayLabel(day) : '';
  $('back-today').hidden = day === state.today;
  $('day-status').textContent = totalsForDay.messages
    ? `${totalsForDay.conversations} konverzací · ${pending ? `${pending} zpráv čeká` : 'vše analyzováno'}`
    : '';
  const attempts = state.analyses.filter((a) => a.day === day).sort((a, b) => b.id - a.id),
    latest = attempts[0],
    success = attempts.find((a) => a.status === 'completed' && a.messageCount > 0);
  const active = attempts.find((a) => ['queued', 'running'].includes(a.status));
  $('analyze').disabled = !canAnalyze(day);
  $('analyze').textContent = active
    ? labels[active.status]
    : success
      ? 'Aktualizovat'
      : 'Analyzovat';
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
    if (pending) report.append(el('p', 'Přibyly nové zprávy. Rozbor můžete aktualizovat.', 'hint'));
  } else if (!active)
    report.append(
      el(
        'p',
        totalsForDay.messages
          ? 'Připraveno k rozboru.'
          : 'Pro tento den zatím nejsou žádné zprávy.',
        'empty'
      )
    );
  $('day-conversations-heading').textContent = `Konverzace (${totalsForDay.conversations})`;
  $('day-conversations').hidden = !totalsForDay.conversations;
  const list = $('current-conversations');
  list.replaceChildren();
  for (const c of dayRows(day).sort((a, b) => b.id - a.id)) list.append(conversationCard(c, day));
}
function renderArchive(days) {
  const archive = $('archive');
  archive.replaceChildren();
  $('archive-count').textContent = `${days.length} dní · ${state.conversations.length} konverzací`;
  for (const day of days) {
    const t = totals(day),
      pending = t.messages - t.analyzed,
      attempts = state.analyses.filter((a) => a.day === day).sort((a, b) => b.id - a.id);
    const group = el('details');
    group.dataset.key = `day-${day}`;
    const summary = el('summary');
    const line = el('span', undefined, 'day-summary');
    line.append(
      el('span', dayLabel(day)),
      el('span', `${t.conversations} konverzací · ${attempts.length} verzí rozboru`, 'subtle'),
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
    if (attempts.length)
      group.append(el('h3', 'Verze rozboru · od nejnovější'), attemptsList(attempts));
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
    const days = [
      ...new Set([
        ...state.conversations.flatMap((c) => c.days.map((d) => d.day)),
        ...state.analyses.map((a) => a.day),
      ]),
    ]
      .sort()
      .reverse();
    const day = selectedDay || state.today;
    const signature = JSON.stringify([day, state.conversations, state.analyses]);
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
    const waitingDays = days.filter((d) => totals(d).messages > totals(d).analyzed);
    $('open-archive').textContent = waitingDays.length
      ? `Archiv (${waitingDays.length})`
      : 'Archiv';
    $('open-archive').title = waitingDays.length
      ? `${waitingDays.length} dní čeká na rozbor`
      : 'Starší dny a rozbory';
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
async function showConversation(id, day) {
  const c = await api('conversations/' + id);
  $('conversation-title').textContent = `#${c.id} · ${c.title}`;
  const content = $('conversation-content');
  content.replaceChildren();
  const messages = c.messages.filter(
    (m) =>
      !day ||
      new Intl.DateTimeFormat('sv-SE', {
        timeZone: 'Europe/Prague',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
      }).format(new Date(m.createdAt)) === day
  );
  if (!messages.length)
    content.append(el('p', c.error || 'Pro vybraný den nejsou dostupné zprávy.'));
  for (const m of messages) {
    const row = el('div', undefined, 'message');
    row.append(
      el('strong', `${m.number}. ${m.role === 'user' ? 'Vy' : 'AI'}`),
      el('p', date(m.createdAt), 'meta'),
      el('pre', m.text)
    );
    content.append(row);
  }
  if (!$('conversation-dialog').open) $('conversation-dialog').showModal();
}
$('close-conversation').onclick = () => $('conversation-dialog').close();
$('close-prompt').onclick = () => $('prompt-dialog').close();
$('open-settings').onclick = () => promptEditor.open($('day').value);
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
refresh().catch((e) => notice(e.message));
setInterval(() => refresh().catch((e) => notice('Spojení s Mentorem selhalo: ' + e.message)), 2500);
