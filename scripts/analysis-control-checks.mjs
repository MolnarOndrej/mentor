import assert from 'node:assert/strict';
import { analysisControl } from '../src/Mentor/wwwroot/analysis-control.js';

const day = '2026-10-18';
const state = { conversations: [], analyses: [], changedDays: [] };
assert.equal(analysisControl(undefined, day).enabled, false);
assert.equal(analysisControl(state, day).status, 'empty');
state.conversations.push({ status: 'ready', days: [{ day, messageCount: 14, analyzedCount: 0 }] });
state.changedDays = [day];
assert.deepEqual(analysisControl(state, day), { enabled: true, status: 'ready', label: 'Analyzovat' });
state.analyses.push({ day, status: 'queued', messageCount: 0 });
assert.equal(analysisControl(state, day).enabled, false);
state.analyses[0].status = 'running';
assert.equal(analysisControl(state, day).status, 'running');
state.analyses[0].status = 'failed';
assert.equal(analysisControl(state, day).enabled, true);
state.analyses[0] = { day, status: 'completed', messageCount: 14 };
state.conversations[0].days[0].analyzedCount = 14;
state.changedDays = [];
assert.deepEqual(analysisControl(state, day), { enabled: true, status: 'ready', label: 'Analyzovat' });
state.conversations.push({ status: 'ready', days: [{ day, messageCount: 2, analyzedCount: 0 }] });
state.changedDays = [day];
assert.deepEqual(analysisControl(state, day), { enabled: true, status: 'ready', label: 'Analyzovat' });
state.analyses.push({ day, status: 'failed', messageCount: 16 });
assert.equal(analysisControl(state, day).enabled, true);
assert.equal(analysisControl(state, '2026-10-19').status, 'empty');
state.conversations[1].days[0].analyzedCount = 2;
state.changedDays = [];
assert.equal(analysisControl(state, day).enabled, true);
state.today = day;
assert.equal(analysisControl(state, day).enabled, true);
state.changedDays = [day];
assert.deepEqual(analysisControl(state, day), { enabled: true, status: 'ready', label: 'Analyzovat' });
state.analyses.push({ day, status: 'running', messageCount: 16 });
assert.equal(analysisControl(state, day).enabled, false);
state.analyses.pop();
state.conversations.forEach((c) => c.status = 'failed');
assert.equal(analysisControl(state, day).enabled, false);
state.conversations = [];
assert.equal(analysisControl(state, day).enabled, false);
state.changedDays = [];
assert.equal(analysisControl(state, day).enabled, false);
state.today = '2026-10-19';
assert.equal(analysisControl(state, day).enabled, false);
console.log('Analysis control checks passed: empty, ready, queued, running, failed, completed, new imports and day isolation.');

state.conversations = [{ status: 'ready', days: [{ day, messageCount: 1 }] }];
for (const status of ['queued', 'running']) {
  state.discussions = [{ day, status }];
  assert.equal(analysisControl(state, day).enabled, false, 'Answer reserves the day');
  assert.equal(analysisControl(state, day).label, 'Probíhá odpověď');
  state.discussions[0].day = '2026-10-19';
  assert.equal(analysisControl(state, day).enabled, true, 'Answer does not reserve other days');
}
for (const status of ['completed', 'failed']) {
  state.discussions = [{ day, status }];
  assert.equal(analysisControl(state, day).enabled, true, 'Finished answer releases the day');
}
