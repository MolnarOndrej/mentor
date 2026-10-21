import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtempSync, mkdirSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Exercises the real HTTP host, SQLite queue, OS child process and OTLP transport.
// The CLI is the deterministic test executable; this never calls a model service.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const suffix = process.platform === 'win32' ? '.exe' : '';
const configuration = process.env.MENTOR_BUILD_CONFIGURATION || 'Debug';
const app = join(root, `src/Mentor/bin/${configuration}/net10.0/Mentor${suffix}`);
const fakeCli = join(root, `tests/Mentor.Checks/bin/${configuration}/net10.0/Mentor.Checks${suffix}`);
const dataRoot = join(root, 'src/Mentor/Data');
mkdirSync(dataRoot, { recursive: true });
const directory = mkdtempSync(join(dataRoot, 'smoke-'));
const seeded = spawnSync(fakeCli, ['--seed-smoke', join(directory, 'mentor.db')], {
  windowsHide: true,
  encoding: 'utf8',
});
assert.equal(seeded.status, 0, seeded.stderr);

const payloads = [];
const collector = createServer((request, response) => {
  const chunks = [];
  request.on('data', (chunk) => chunks.push(chunk));
  request.on('end', () => {
    payloads.push({ path: request.url, body: Buffer.concat(chunks) });
    response.writeHead(200, { 'Content-Type': 'application/x-protobuf' });
    response.end();
  });
});
await new Promise((done) => collector.listen(0, '127.0.0.1', done));
const environment = {
  ...process.env,
  Mentor__DataDirectory: directory,
  Mentor__CodexExecutable: fakeCli,
  MENTOR_CAPTURE_FILE: join(directory, 'stdin.txt'),
  OTEL_EXPORTER_OTLP_ENDPOINT: `http://127.0.0.1:${collector.address().port}`,
  OTEL_EXPORTER_OTLP_PROTOCOL: 'http/protobuf',
  OTEL_METRIC_EXPORT_INTERVAL: '500',
  OTEL_BSP_SCHEDULE_DELAY: '200',
};
delete environment.MENTOR_FAKE_MODE;
const server = spawn(app, [], { cwd: join(root, 'src/Mentor'), env: environment, windowsHide: true });
let diagnostics = '';
for (const stream of [server.stdout, server.stderr]) {
  stream.on('data', (chunk) => { if (diagnostics.length < 20_000) diagnostics += chunk; });
}
const base = 'http://127.0.0.1:5178';
async function request(path, body, method = 'POST') {
  const response = await fetch(base + path, body === undefined ? {} : {
    method,
    headers: { 'Content-Type': 'application/json', 'X-Mentor-Request': '1' },
    body: JSON.stringify(body),
  });
  assert.ok(response.ok, `${path}: ${response.status} ${await response.clone().text()}`);
  return response.json();
}
async function until(action, timeout = 15_000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) throw Error(`Server exited: ${diagnostics}`);
    try { const result = await action(); if (result) return result; } catch { /* Startup/flush pending. */ }
    await new Promise((done) => setTimeout(done, 100));
  }
  throw Error(`Timed out. ${diagnostics}`);
}

try {
  const state = await until(() => request('/api/state'));
  assert.equal(state.conversations.length, 2);
  assert.deepEqual(state.settings.runtime, { pollIntervalSeconds: 5, dailyTime: '22:00' });
  const runtime = { pollIntervalSeconds: 1, dailyTime: '23:59' };
  assert.deepEqual(await request('/api/settings', runtime, 'PUT'), runtime);
  assert.deepEqual((await request('/api/state')).settings.runtime, runtime);
  for (const invalidSettings of [
    { pollIntervalSeconds: 0, dailyTime: '22:00' },
    { pollIntervalSeconds: 3.5, dailyTime: '22:00' },
    { pollIntervalSeconds: 3, dailyTime: '24:00' },
    { pollIntervalSeconds: 3, dailyTime: null },
  ]) {
    const response = await fetch(base + '/api/settings', {
      method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-Mentor-Request': '1' },
      body: JSON.stringify(invalidSettings),
    });
    assert.equal(response.status, 400);
    assert.deepEqual((await request('/api/state')).settings.runtime, runtime);
  }
  await until(async () => (await request('/api/operations')).worker.pollIntervalSeconds === 1);
  const readiness = await request('/health/ready');
  assert.equal(readiness.status, 'Healthy');
  const day = state.conversations[0].days[0].day;
  const template = (await request('/api/prompt', { prompt: state.settings.prompt }, 'PUT')).template;
  const accepted = await request('/api/analyses', { day, prompt: template });
  const report = await until(async () => {
    const result = await request(`/api/analyses/${accepted.id}`);
    assert.notEqual(result.status, 'failed', result.error);
    return result.status === 'completed' ? result : null;
  });
  assert.equal(report.conversationIds.length, 2);
  const fullPrompt = (await request(`/api/analyses/${report.id}/prompt`)).prompt;
  assert.equal(fullPrompt, readFileSync(environment.MENTOR_CAPTURE_FILE, 'utf8'));
  assert.ok(fullPrompt.includes('PRIVATE_SMOKE_MARKER'));
  assert.deepEqual(await request(`/api/analyses/${report.id}/discussion`), []);
  const question = await request(`/api/analyses/${report.id}/discussion`, { question: 'PRIVATE_DISCUSSION_MARKER' });
  const discussion = await until(async () => {
    const turns = await request(`/api/analyses/${report.id}/discussion`);
    assert.notEqual(turns[0]?.status, 'failed', turns[0]?.error);
    return turns[0]?.status === 'completed' ? turns : null;
  });
  assert.equal(discussion[0].id, question.id);
  assert.equal(discussion[0].question, 'PRIVATE_DISCUSSION_MARKER');
  assert.equal(discussion[0].answer, 'Testovací rozbor');
  assert.equal((await request('/api/state')).discussions[0].status, 'completed');
  const oversizedQuestion = await fetch(base + `/api/analyses/${report.id}/discussion`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Mentor-Request': '1' },
    body: JSON.stringify({ question: 'x'.repeat(240_000) }),
  });
  assert.equal(oversizedQuestion.status, 400);
  assert.equal((await request(`/api/analyses/${report.id}/discussion`)).length, 1);
  const operations = await request('/api/operations');
  assert.equal(operations.queue.pendingAnalyses, 0);
  await until(() => payloads.some(p => p.path === '/v1/traces' && p.body.includes(Buffer.from('mentor.analysis'))) &&
    payloads.some(p => p.path === '/v1/metrics' && p.body.includes(Buffer.from('mentor.jobs'))));
  const exported = Buffer.concat(payloads.map(p => p.body)).toString();
  assert.ok(exported.includes('mentor.analysis'));
  assert.ok(exported.includes('mentor.jobs'));
  assert.ok(!exported.includes('PRIVATE_SMOKE_MARKER'));
  assert.ok(!exported.includes('PRIVATE_DISCUSSION_MARKER'));
  const logs = readdirSync(join(directory, 'logs')).map(file => readFileSync(join(directory, 'logs', file), 'utf8')).join('');
  assert.ok(logs.includes('Background job'));
  assert.ok(!logs.includes('PRIVATE_SMOKE_MARKER'));
  assert.ok(!logs.includes('PRIVATE_DISCUSSION_MARKER'));
  assert.equal((await fetch(base + '/api/analyses', { method: 'POST' })).status, 403);
  const invalid = await fetch(base + '/api/analyses', {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Mentor-Request': '1' },
    body: JSON.stringify({ day: 'invalid' }),
  });
  assert.equal(invalid.status, 400);
  assert.ok((await invalid.json()).traceId);
  console.log(JSON.stringify({ readiness: 'Healthy', conversationsPerAnalysis: 2, exactPrompt: true,
    tracesExported: true, metricsExported: true, privateContentExcluded: true,
    databaseSettings: true, liveWorkerPolling: true, settingsValidation: true, discussionAnswer: true,
    discussionContextLimit: true, logsDirectory: directory }));
} finally {
  server.kill();
  await new Promise((done) => server.exitCode !== null ? done() : server.once('exit', done));
  await new Promise((done) => collector.close(done));
}
