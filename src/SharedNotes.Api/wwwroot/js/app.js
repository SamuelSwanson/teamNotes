// Minimal demo client. All data comes from the REST API; the selected demo employee is sent in X-Employee-Id.
'use strict';

const $ = (id) => document.getElementById(id);
const STORAGE_KEY = 'sharedNotes.employeeId';

const state = { teams: [], topics: [], editing: null /* note being edited, or null for create */ };

// ---------- API helper ----------

class ApiError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

async function api(method, path, body) {
  const headers = { 'X-Employee-Id': $('employee').value };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (res.status === 204) return null;
  const data = await res.json().catch(() => null);
  if (!res.ok) {
    const detail = data?.errors ? Object.values(data.errors).flat().join(' ') : (data?.detail || data?.title || res.statusText);
    throw new ApiError(res.status, detail);
  }
  return data;
}

// ---------- UI helpers ----------

let messageTimer;
function showMessage(text, isError = false) {
  const el = $('message');
  el.textContent = text;
  el.classList.toggle('error', isError);
  el.classList.remove('hidden');
  clearTimeout(messageTimer);
  if (!isError) messageTimer = setTimeout(() => el.classList.add('hidden'), 4000);
}

function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);   // textContent only: never innerHTML with API data
  node.append(...children);
  return node;
}

function option(value, text) { return el('option', { value, textContent: text }); }

function fillSelect(select, firstLabel, items, valueKey, textKey) {
  const current = select.value;
  select.replaceChildren(...(firstLabel === null ? [] : [option('', firstLabel)]),
    ...items.map((i) => option(i[valueKey], i[textKey])));
  if ([...select.options].some((o) => o.value === current)) select.value = current;
}

// ---------- Loading ----------

async function loadReferenceData() {
  [state.teams, state.topics] = await Promise.all([api('GET', '/api/teams'), api('GET', '/api/topics')]);
  fillSelect($('f-team'), 'Any team', state.teams, 'teamId', 'name');
  fillSelect($('f-topic'), 'Any topic', state.topics, 'topicId', 'name');
}

async function loadNotes() {
  const params = new URLSearchParams();
  const add = (k, v) => { if (v) params.set(k, v); };
  add('search', $('f-search').value.trim());
  add('visibility', $('f-visibility').value);
  add('teamId', $('f-team').value);
  add('topicId', $('f-topic').value);

  $('status').textContent = 'Loading…';
  try {
    const notes = await api('GET', '/api/notes?' + params);
    renderNotes(notes);
    $('status').textContent = notes.length ? `${notes.length} note(s)` : 'No notes match.';
  } catch (e) {
    $('notes').replaceChildren();
    $('status').textContent = '';
    showMessage('Could not load notes: ' + e.message, true);
  }
}

async function refreshAll() {
  try { await loadReferenceData(); } catch (e) { showMessage('Could not load teams/topics: ' + e.message, true); }
  await loadNotes();
}

// ---------- Rendering ----------

function renderNotes(notes) {
  $('notes').replaceChildren(...notes.map((n) => {
    const preview = n.content.length > 160 ? n.content.slice(0, 160) + '…' : n.content;
    const vis = n.visibility === 'Team' ? `Team: ${n.teamName}` : n.visibility;
    const li = el('li', { className: 'note' },
      el('h3', { textContent: n.title }),
      el('p', { textContent: preview }),
      el('div', { className: 'meta' },
        el('span', { className: 'tag vis', textContent: vis }),
        ...n.topics.map((t) => el('span', { className: 'tag', textContent: t.name })),
        el('span', { textContent: `By ${n.createdByName}` }),
        el('span', { textContent: `Updated ${new Date(n.updatedAt).toLocaleString()} by ${n.updatedByName}` })),
      el('div', { className: 'actions' }));
    const actions = li.querySelector('.actions');
    actions.append(el('button', { type: 'button', textContent: 'Edit', onclick: () => openEditor(n.noteId) }));
    if (n.canDelete) {
      actions.append(el('button', { type: 'button', className: 'danger', textContent: 'Delete', onclick: () => deleteNote(n) }));
    }
    return li;
  }));
}

// ---------- Editor ----------

function updateTeamRow() { $('n-team-row').classList.toggle('hidden', $('n-visibility').value !== 'Team'); }

function setEditorError(text) {
  $('editor-error').textContent = text || '';
  $('editor-error').classList.toggle('hidden', !text);
}

function populateEditor(note) {
  state.editing = note;
  $('editor-title').textContent = note ? 'Edit note' : 'New note';
  $('n-title').value = note?.title ?? '';
  $('n-content').value = note?.content ?? '';
  $('n-visibility').value = note?.visibility ?? 'Private';
  fillSelect($('n-team'), null, state.teams, 'teamId', 'name');
  if (note?.teamId) $('n-team').value = note.teamId;
  const selected = new Set((note?.topics ?? []).map((t) => t.topicId));
  $('n-topics').replaceChildren(...state.topics.map((t) => {
    const box = el('input', { type: 'checkbox', value: t.topicId, checked: selected.has(t.topicId) });
    return el('label', {}, box, t.name);
  }));
  updateTeamRow();
  setEditorError('');
  $('reload-note').classList.add('hidden');
  $('save').disabled = false;
}

async function openEditor(noteId) {
  try {
    // Fetch the latest copy so the Version token we send back is the one we actually displayed.
    populateEditor(noteId ? await api('GET', `/api/notes/${noteId}`) : null);
    $('editor').showModal();
  } catch (e) {
    showMessage('Could not open note: ' + e.message, true);
  }
}

async function saveNote(event) {
  event.preventDefault();
  const visibility = $('n-visibility').value;
  const body = {
    title: $('n-title').value.trim(),
    content: $('n-content').value,
    visibility,
    teamId: visibility === 'Team' ? Number($('n-team').value) || null : null,
    topicIds: [...$('n-topics').querySelectorAll('input:checked')].map((b) => Number(b.value)),
  };
  if (state.editing) body.expectedVersion = state.editing.version;

  $('save').disabled = true;
  try {
    if (state.editing) await api('PUT', `/api/notes/${state.editing.noteId}`, body);
    else await api('POST', '/api/notes', body);
    $('editor').close();
    showMessage(state.editing ? 'Note updated.' : 'Note created.');
    await loadNotes();
  } catch (e) {
    if (e.status === 409) {
      setEditorError('This note was modified by someone else since you opened it. Reload the latest version before editing further (your unsaved changes will be replaced).');
      $('reload-note').classList.remove('hidden');
    } else {
      setEditorError(e.message);
    }
    $('save').disabled = e.status === 409;   // block saving the stale copy until "Reload latest" is used
  }
}

async function reloadEditingNote() {
  try {
    populateEditor(await api('GET', `/api/notes/${state.editing.noteId}`));
    showMessage('Reloaded the latest version.');
  } catch (e) {
    setEditorError(e.message);
  }
}

async function deleteNote(note) {
  if (!confirm(`Delete "${note.title}"? This cannot be undone.`)) return;
  try {
    await api('DELETE', `/api/notes/${note.noteId}`);
    showMessage('Note deleted.');
    await loadNotes();
  } catch (e) {
    showMessage('Could not delete: ' + e.message, true);
  }
}

// ---------- Startup ----------

async function init() {
  try {
    const employees = await (await fetch('/api/demo/employees')).json();
    fillSelect($('employee'), null, employees, 'employeeId', 'name');
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved && employees.some((e) => String(e.employeeId) === saved)) $('employee').value = saved;
  } catch {
    $('status').textContent = '';
    showMessage('Could not reach the API.', true);
    return;
  }

  $('employee').addEventListener('change', () => { localStorage.setItem(STORAGE_KEY, $('employee').value); refreshAll(); });
  let searchTimer;
  $('f-search').addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(loadNotes, 250); });
  for (const id of ['f-visibility', 'f-team', 'f-topic']) $(id).addEventListener('change', loadNotes);
  $('new-note').addEventListener('click', () => openEditor(null));
  $('n-visibility').addEventListener('change', updateTeamRow);
  $('note-form').addEventListener('submit', saveNote);
  $('cancel').addEventListener('click', () => $('editor').close());
  $('reload-note').addEventListener('click', reloadEditingNote);

  localStorage.setItem(STORAGE_KEY, $('employee').value);
  await refreshAll();
}

init();
