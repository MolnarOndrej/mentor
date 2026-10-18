export const $ = (id) => document.getElementById(id);

export const date = (value) =>
  new Intl.DateTimeFormat('cs-CZ', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Europe/Prague',
  }).format(new Date(value));

export const dayLabel = (day) =>
  new Intl.DateTimeFormat('cs-CZ', { dateStyle: 'long', timeZone: 'Europe/Prague' }).format(
    new Date(day + 'T12:00:00Z')
  );

export function el(tag, text, cls) {
  const e = document.createElement(tag);
  if (text !== undefined) e.textContent = text;
  if (cls) e.className = cls;
  return e;
}

// Only DOM text nodes, never HTML received from a model or imported conversation.
export function formatted(text) {
  const box = el('div', undefined, 'formatted');
  function inline(parent, value) {
    for (const [i, part] of value.split(/\*\*(.*?)\*\*/g).entries())
      parent.append(i % 2 ? el('strong', part) : document.createTextNode(part));
  }
  for (const block of text.split(/\n\s*\n/)) {
    if (/^#{1,3} /.test(block)) {
      const h = el('h4');
      inline(h, block.replace(/^#{1,3} /, ''));
      box.append(h);
    } else if (/^\d+\. /.test(block)) {
      const list = el('ol');
      for (const line of block.split(/\n(?=\d+\. )/)) {
        const item = el('li');
        inline(item, line.replace(/^\d+\. /, ''));
        list.append(item);
      }
      box.append(list);
    } else {
      const p = el('p');
      inline(p, block);
      box.append(p);
    }
  }
  return box;
}
