import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { request as rawRequest } from 'node:http';

// Real local HTTP host and SQLite, no model or source URL downloads.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const configuration = process.env.MENTOR_BUILD_CONFIGURATION || 'Release';
const suffix = process.platform === 'win32' ? '.exe' : '';
const app = join(root, `src/Mentor/bin/${configuration}/net10.0/Mentor${suffix}`);
const fakeCli = join(root, `tests/Mentor.Checks/bin/${configuration}/net10.0/Mentor.Checks${suffix}`);
const dataRoot = join(root, 'src/Mentor/Data');
mkdirSync(dataRoot, { recursive: true });
const directory = mkdtempSync(join(dataRoot, 'mcp-smoke-'));
const base = 'http://127.0.0.1:5178';
const token = randomBytes(32).toString('hex');
const secretMarker = 'PRIVATE_MCP_CONTENT_' + randomBytes(8).toString('hex');
let server;
let diagnostics = '';
let nextId = 0;

try {
  await fetch(base + '/health/live');
  throw Error('Port 5178 is in use; stop Mentor before running this check.');
} catch (error) {
  if (error.message.includes('Port 5178')) throw error;
}

async function start(enabled) {
  const env = { ...process.env, Mentor__DataDirectory: directory, Mentor__CodexExecutable: fakeCli };
  delete env.OTEL_EXPORTER_OTLP_ENDPOINT;
  delete env.Mentor__Mcp__Token;
  if (enabled) env.Mentor__Mcp__Token = token;
  server = spawn(app, [], { cwd: join(root, 'src/Mentor'), env, windowsHide: true });
  for (const stream of [server.stdout, server.stderr])
    stream.on('data', (chunk) => { diagnostics += chunk; });
  const deadline = Date.now() + 15_000;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) throw Error(`Server exited: ${diagnostics}`);
    try { if ((await fetch(base + '/health/live')).ok) return; } catch { /* Starting. */ }
    await new Promise((done) => setTimeout(done, 100));
  }
  throw Error(`Startup timed out: ${diagnostics}`);
}

async function stop() {
  if (!server || server.exitCode !== null || server.signalCode !== null) return;
  const exited = new Promise((done) => server.once('exit', done));
  server.kill();
  await exited;
}

async function http(body, headers = {}, method = 'POST', path = '/mcp') {
  return fetch(base + path, {
    method,
    headers: {
      'Content-Type': 'application/json', Accept: 'application/json, text/event-stream',
      Authorization: `Bearer ${token}`, ...headers,
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
}

async function rpc(method, params) {
  const response = await http({ jsonrpc: '2.0', id: ++nextId, method, params });
  const text = await response.text();
  assert.equal(response.status, 200, text);
  const result = response.headers.get('content-type')?.includes('text/event-stream')
    ? JSON.parse(text.split('\n').filter((line) => line.startsWith('data: ')).at(-1).slice(6))
    : JSON.parse(text);
  assert.equal(result.error, undefined, JSON.stringify(result.error));
  return result.result;
}

async function statusWithHost(host) {
  return new Promise((done, fail) => {
    const request = rawRequest(base + '/mcp', { method: 'POST', headers: { Host: host } }, (response) => {
      response.resume();
      done(response.statusCode);
    });
    request.on('error', fail);
    request.end();
  });
}

async function call(conversation) {
  return rpc('tools/call', { name: 'mentor_upsert_conversation', arguments: { conversation } });
}

async function api(path, body) {
  const response = await fetch(base + '/api/' + path, body === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Mentor-Request': '1' }, body: JSON.stringify(body),
  });
  assert.ok(response.ok, await response.clone().text());
  return response.json();
}

try {
  await start(false);
  assert.equal((await http({})).status, 404, 'MCP disabled without a token');
  await stop();
  await start(true);
  for (const authorization of ['', 'Bearer wrong', `Basic ${token}`, `Bearer ${token}x`])
    assert.equal((await http({}, { Authorization: authorization })).status, 401);
  assert.equal((await http({}, { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await http(undefined, { Origin: 'https://evil.example' }, 'GET')).status, 403);
  assert.equal(await statusWithHost('evil.example:5178'), 403);
  assert.equal(await statusWithHost('localhost:9999'), 403);
  assert.equal((await http({}, {}, 'POST', '/api/conversations')).status, 403, 'UI API still needs CSRF header');
  assert.equal((await http({}, { 'X-Mentor-Request': '1', Origin: 'https://evil.example' }, 'POST', '/api/conversations')).status, 403);
  assert.equal((await http({ jsonrpc: '2.0', id: 1, method: 'ping' }, {}, 'POST', '/mcp/')).status, 200);
  assert.equal((await http({})).status, 400, 'malformed protocol envelope is a client error');
  const initialized = await rpc('initialize', {
    protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'mentor-checks', version: '1.0' },
  });
  assert.ok(initialized.capabilities.tools);
  const notification = await http({ jsonrpc: '2.0', method: 'notifications/initialized' });
  assert.equal(notification.status, 202);
  const tools = (await rpc('tools/list', {})).tools;
  assert.deepEqual(tools.map((tool) => tool.name), ['mentor_upsert_conversation']);
  assert.ok(tools[0].inputSchema.properties.conversation);
  assert.ok(!tools[0].inputSchema.properties.repository, 'repository is injected, never client-controlled');
  assert.ok(tools[0].outputSchema);

  const conversation = {
    source: 'codex', externalId: 'wire-stable', title: 'Private conversation', contentKind: 'summary',
    url: 'https://chatgpt.com/c/private', messages: [{ role: 'assistant', text: secretMarker, number: 1 }],
  };
  const first = await call(conversation);
  assert.ok(!first.isError, JSON.stringify(first));
  const saved = first.structuredContent;
  assert.ok(saved.added && saved.id > 0);
  assert.equal(saved.contentKind, 'summary');
  const state = await api('state');
  assert.equal(saved.day, state.today);
  assert.equal(state.conversations.length, 1);
  const direct = await api(`conversations/${saved.id}`);
  assert.equal(direct.status, 'ready');
  assert.equal(direct.source, 'codex');
  assert.deepEqual(direct.messages, conversation.messages);

  conversation.title = 'Updated';
  conversation.contentKind = 'transcript';
  conversation.url = null;
  conversation.messages.push({ role: 'user', text: 'New text', number: 5 });
  const updated = (await call(conversation)).structuredContent;
  assert.equal(updated.id, saved.id);
  assert.equal(updated.added, false);
  assert.equal(updated.messageCount, 2);
  for (const invalid of [
    { ...conversation, contentKind: 'partial' }, { ...conversation, contentKind: null },
    { ...conversation, messages: [{ role: 'user', text: 'Text', number: 0 }] },
    { ...conversation, messages: [{ role: 'user', text: 'x'.repeat(240_001), number: 1 }] },
  ]) assert.equal((await call(invalid)).isError, true);
  const missingKind = { ...conversation };
  delete missingKind.contentKind;
  assert.equal((await call(missingKind)).isError, true, 'content kind cannot be silently defaulted');
  const oversized = await fetch(base + '/mcp', {
    method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' },
    body: ' '.repeat(2 * 1024 * 1024 + 1),
  });
  assert.equal(oversized.status, 413);
  assert.equal((await api('state')).conversations.length, 1);
  assert.deepEqual((await api(`conversations/${saved.id}`)).messages, conversation.messages);
  const refresh = await api('conversations/refresh-today', {});
  assert.equal(refresh.checkedCount, 0);
  await stop();
  await start(true);
  assert.equal((await api('state')).conversations.length, 1, 'restart preserves direct data');
  assert.equal((await call(conversation)).structuredContent.id, saved.id);
  await stop();
  const logs = readdirSync(join(directory, 'logs')).map((name) => readFileSync(join(directory, 'logs', name), 'utf8')).join('');
  for (const output of [diagnostics, logs]) {
    assert.ok(!output.includes(secretMarker), 'MCP payload stays out of logs');
    assert.ok(!output.includes(token), 'MCP bearer token stays out of logs');
  }
  console.log('MCP HTTP checks passed: auth, host/origin, lifecycle, schema, upsert, validation, limits, refresh, restart and log privacy.');
} finally {
  await stop();
}
