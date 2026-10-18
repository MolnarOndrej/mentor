export async function api(path, method = 'GET', body) {
  const r = await fetch('/api/' + path, {
    method,
    headers: { 'Content-Type': 'application/json', 'X-Mentor-Request': '1' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!r.ok) {
    let error;
    try {
      error = (await r.json()).error;
    } catch {}
    throw Error(error || `Požadavek selhal (HTTP ${r.status}).`);
  }
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}
