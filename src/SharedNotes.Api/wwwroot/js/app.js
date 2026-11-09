// Minimal client. All data comes from the REST API; the session lives in an HttpOnly cookie set by /api/auth/login.
'use strict';

const $ = (id) => document.getElementById(id);

const PAGE_SIZE = 10;
const state = { page: 1, me: null, teams: [], topics: [], editing: null /* note being edited, or null for create */ };

// ---------- API helper ----------

class ApiError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

async function api(method, path, body) {
  const isForm = body instanceof FormData;   // file uploads: let the browser set the multipart boundary
  const headers = body === undefined || isForm ? {} : { 'Content-Type': 'application/json' };
  const res = await fetch(path, { method, headers, body: body === undefined || isForm ? body : JSON.stringify(body) });
  if (res.status === 204) return null;
  const data = await res.json().catch(() => null);
  if (res.status === 401 && state.me && !path.startsWith('/api/auth/')) {
    showLogin('Your session has ended. Please sign in again.');   // expired or signed out elsewhere
    throw new ApiError(401, 'Not signed in');
  }
  if (!res.ok) {
    const detail = data?.errors ? Object.values(data.errors).flat().join(' ') : (data?.detail || data?.title || res.statusText);
    throw new ApiError(res.status, detail);
  }
  api.lastHeaders = res.headers;   // lets callers read e.g. X-Total-Count after the call
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
  params.set('page', state.page);
  params.set('pageSize', PAGE_SIZE);

  $('status').textContent = 'Loading…';
  try {
    const notes = await api('GET', '/api/notes?' + params);
    const total = Number(api.lastHeaders.get('X-Total-Count') ?? notes.length);
    const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
    if (state.page > pages) { state.page = pages; return loadNotes(); }   // e.g. the last note on this page was deleted
    renderNotes(notes);
    renderPager(total, pages);
    $('status').textContent = total ? `${total} note(s)` : 'No notes match.';
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
      ...(n.attachments.length ? [el('div', { className: 'meta files' }, el('span', { textContent: 'Files:' }), ...n.attachments.map((a) => attachmentLinkFor(n.noteId, a)))] : []),
      el('div', { className: 'actions' }));
    const actions = li.querySelector('.actions');
    actions.append(el('button', { type: 'button', textContent: 'Edit', onclick: () => openEditor(n.noteId) }));
    actions.append(el('button', { type: 'button', textContent: 'History', onclick: () => openHistory(n) }));
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
  $('attachments-box').classList.toggle('hidden', !note);   // files can be added once the note exists
  renderAttachmentList(note?.attachments ?? []);
  setEditorError('');
  $('reload-note').classList.add('hidden');
  $('save').disabled = false;
}

async function openEditor(noteId) {
  try {
    // Fetch the latest copy so the Version token we send back is the one we actually displayed.
    populateEditor(noteId ? await api('GET', `/api/notes/${noteId}`) : null);
    $('editor').showModal();
    if (noteId) beginPresence(noteId);
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
    changedBy = null;
    renderEditorNotice();
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


// ---------- Session ----------

let demoInfo = null;   // set only when the server has Demo:QuickSwitch enabled

async function loadDemoInfo() {
  try {
    const res = await fetch('/api/demo/info');
    demoInfo = res.ok ? await res.json() : null;
  } catch { demoInfo = null; }
  $('demo-banner').classList.toggle('hidden', !demoInfo);
  $('demo-hint').classList.toggle('hidden', !demoInfo);
  if (demoInfo) {
    $('demo-hint').textContent = 'Demo accounts (password "' + demoInfo.password + '"): ' +
      demoInfo.employees.map((e) => `${e.email} (${e.role})`).join(', ');
  }
}

function setLoginError(text) {
  $('login-error').textContent = text || '';
  $('login-error').classList.toggle('hidden', !text);
}

function showLogin(notice) {
  state.me = null;
  stopEvents();
  $('app').classList.add('hidden');
  $('session').classList.add('hidden');
  $('login').classList.remove('hidden');
  $('l-password').value = '';
  setLoginError(notice);
  loadDemoInfo();
}

async function showApp(me) {
  state.me = me;
  state.page = 1;
  startEvents();
  $('login').classList.add('hidden');
  $('app').classList.remove('hidden');
  $('session').classList.remove('hidden');
  $('whoami').textContent = `${me.name} (${me.role})`;

  // Demo quick-switch: only available when the server allows it.
  await loadDemoInfo();
  $('switch-row').classList.toggle('hidden', !demoInfo);
  if (demoInfo) {
    fillSelect($('employee'), null, demoInfo.employees, 'employeeId', 'name');
    $('employee').value = me.employeeId;
  }
  await refreshAll();
}

async function onLogin(event) {
  event.preventDefault();
  $('l-submit').disabled = true;
  try {
    const me = await api('POST', '/api/auth/login', { email: $('l-email').value.trim(), password: $('l-password').value });
    await showApp(me);
  } catch (e) {
    setLoginError(e.status === 429 ? 'Too many attempts. Wait a minute and try again.' : e.message);
  } finally {
    $('l-submit').disabled = false;
  }
}

async function onSwitchEmployee() {
  try {
    const res = await fetch('/api/demo/login/' + $('employee').value, { method: 'POST' });
    if (!res.ok) throw new ApiError(res.status, 'Could not switch employee.');
    await showApp(await api('GET', '/api/auth/me'));
  } catch (e) {
    showMessage(e.message, true);
  }
}

async function onChangePassword(event) {
  event.preventDefault();
  try {
    await api('POST', '/api/auth/change-password', { currentPassword: $('p-current').value, newPassword: $('p-new').value });
    $('password-dialog').close();
    showMessage('Password changed.');
  } catch (e) {
    $('password-error').textContent = e.message;
    $('password-error').classList.remove('hidden');
  }
}

// ---------- Startup ----------

function init() {
  $('login-form').addEventListener('submit', onLogin);
  $('logout').addEventListener('click', async () => { await api('POST', '/api/auth/logout').catch(() => {}); showLogin(''); });
  $('employee').addEventListener('change', onSwitchEmployee);
  $('change-password').addEventListener('click', () => {
    $('password-form').reset();
    $('password-error').classList.add('hidden');
    $('password-dialog').showModal();
  });
  $('p-cancel').addEventListener('click', () => $('password-dialog').close());
  $('password-form').addEventListener('submit', onChangePassword);

  let searchTimer;
  $('f-search').addEventListener('input', () => { clearTimeout(searchTimer); state.page = 1; searchTimer = setTimeout(loadNotes, 250); });
  for (const id of ['f-visibility', 'f-team', 'f-topic']) $(id).addEventListener('change', () => { state.page = 1; loadNotes(); });
  $('new-note').addEventListener('click', () => openEditor(null));
  $('n-visibility').addEventListener('change', updateTeamRow);
  $('note-form').addEventListener('submit', saveNote);
  $('cancel').addEventListener('click', () => $('editor').close());
  $('reload-note').addEventListener('click', reloadEditingNote);

  // Already signed in (valid cookie)? Go straight to the app.
  fetch('/api/auth/me').then((res) => (res.ok ? res.json() : null)).then((me) => (me ? showApp(me) : showLogin('')))
    .catch(() => showLogin('Could not reach the API.'));
}

init();

// ---------- History ----------

async function openHistory(note) {
  $('history-title').textContent = `History: ${note.title}`;
  $('history-list').replaceChildren(el('p', { textContent: 'Loading…' }));
  $('history-dialog').showModal();
  try {
    const revisions = await api('GET', `/api/notes/${note.noteId}/history`);
    $('history-list').replaceChildren(...revisions.map((r) => {
      const where = r.visibility === 'Team' ? `Team: ${r.teamName}` : r.visibility;
      const details = el('details', { className: 'revision' },
        el('summary', { textContent: `${r.action} by ${r.editedByName}, ${new Date(r.editedAt).toLocaleString()}: ${r.title}` }),
        el('div', { className: 'meta', textContent: `${where}${r.topics ? ' · Topics: ' + r.topics : ''}` }),
        el('pre', { textContent: r.content }));
      return details;
    }));
  } catch (e) {
    $('history-list').replaceChildren(el('p', { className: 'message error', textContent: e.message }));
  }
}

$('history-close').addEventListener('click', () => $('history-dialog').close());

// ---------- Live updates and "being edited" notices ----------

let eventSource = null;
let heartbeatTimer = null;
let refreshTimer = null;
const otherEditors = new Map();   // employeeId -> name, for the note open in the editor
let changedBy = null;             // set when someone else saved/deleted the note that is open in the editor

function openNoteId() { return $('editor').open && state.editing ? state.editing.noteId : null; }

function renderEditorNotice() {
  const parts = [];
  if (changedBy) parts.push(changedBy.deleted
    ? `${changedBy.name} deleted this note.`
    : `${changedBy.name} saved a newer version. Use "Reload latest" before saving, or your save will be rejected.`);
  if (otherEditors.size) parts.push(`${[...otherEditors.values()].join(', ')} ${otherEditors.size > 1 ? 'are' : 'is'} also editing this note.`);
  $('editor-notice').textContent = parts.join(' ');
  $('editor-notice').classList.toggle('hidden', parts.length === 0);
  if (changedBy && !changedBy.deleted) $('reload-note').classList.remove('hidden');
}

async function beginPresence(noteId) {
  otherEditors.clear();
  changedBy = null;
  renderEditorNotice();
  const beat = () => api('PUT', `/api/notes/${noteId}/editing`).catch(() => {});
  beat();
  clearInterval(heartbeatTimer);
  heartbeatTimer = setInterval(beat, 20000);   // server forgets editors after ~45s without a heartbeat
  try {
    for (const e of await api('GET', `/api/notes/${noteId}/editors`)) otherEditors.set(e.employeeId, e.name);
    renderEditorNotice();
  } catch { /* the notice is a convenience; ignore failures */ }
}

function endPresence() {
  clearInterval(heartbeatTimer);
  if (state.editing && state.me) {
    fetch(`/api/notes/${state.editing.noteId}/editing`, { method: 'DELETE', keepalive: true }).catch(() => {});
  }
  otherEditors.clear();
  changedBy = null;
}

function onNoteEvent(type, data) {
  if (data.actorId === state.me?.employeeId) {
    if (type === 'note-changed' || type === 'note-deleted') scheduleRefresh();   // keep my other tabs fresh too
    return;
  }
  if (type === 'editing') {
    if (data.noteId === openNoteId()) {
      if (data.active) otherEditors.set(data.actorId, data.actorName); else otherEditors.delete(data.actorId);
      renderEditorNotice();
    }
    return;
  }
  scheduleRefresh();
  if (data.noteId === openNoteId()) {
    changedBy = { name: data.actorName, deleted: type === 'note-deleted' };
    renderEditorNotice();
  }
}

function scheduleRefresh() {
  clearTimeout(refreshTimer);
  refreshTimer = setTimeout(loadNotes, 300);   // coalesce bursts of events into one list refresh
}

function startEvents() {
  stopEvents();
  eventSource = new EventSource('/api/events');   // sends the session cookie; the browser reconnects automatically
  for (const type of ['note-changed', 'note-deleted', 'editing']) {
    eventSource.addEventListener(type, (e) => onNoteEvent(type, JSON.parse(e.data)));
  }
}

function stopEvents() {
  if (eventSource) eventSource.close();
  eventSource = null;
}

$('editor').addEventListener('close', endPresence);
window.addEventListener('pagehide', () => { if (openNoteId()) endPresence(); });

// ---------- Pagination ----------

function renderPager(total, pages) {
  $('pager').classList.toggle('hidden', pages <= 1);
  $('page-info').textContent = `Page ${state.page} of ${pages}`;
  $('prev-page').disabled = state.page <= 1;
  $('next-page').disabled = state.page >= pages;
}

function goToPage(delta) { state.page += delta; loadNotes(); }
$('prev-page').addEventListener('click', () => goToPage(-1));
$('next-page').addEventListener('click', () => goToPage(1));

// ---------- Attachments ----------

function formatSize(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  return bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}


function renderAttachmentList(attachments) {
  $('a-list').replaceChildren(...attachments.map((a) => el('li', {},
    attachmentLinkFor(state.editing?.noteId, a),
    el('button', { type: 'button', className: 'danger', textContent: 'Remove', onclick: () => removeAttachment(a) }))));
}

// Downloads go through the authorized API (the session cookie is sent); the server forces a download.
function attachmentLinkFor(noteId, a) {
  return el('a', { href: `/api/notes/${noteId}/attachments/${a.attachmentId}`, textContent: `${a.fileName} (${formatSize(a.sizeBytes)})` });
}

async function reloadAttachments() {
  const noteId = state.editing.noteId;
  renderAttachmentList(await api('GET', `/api/notes/${noteId}/attachments`));
  await loadNotes();
}

async function onAttachmentChosen() {
  const file = $('a-file').files[0];
  if (!file || !state.editing) return;
  setEditorError('');
  const form = new FormData();
  form.append('file', file);
  try {
    await api('POST', `/api/notes/${state.editing.noteId}/attachments`, form);
    showMessage('File attached.');
    await reloadAttachments();
  } catch (e) {
    setEditorError(e.message);
  } finally {
    $('a-file').value = '';
  }
}

async function removeAttachment(a) {
  if (!confirm(`Remove "${a.fileName}"?`)) return;
  try {
    await api('DELETE', `/api/notes/${state.editing.noteId}/attachments/${a.attachmentId}`);
    showMessage('File removed.');
    await reloadAttachments();
  } catch (e) {
    setEditorError(e.message);
  }
}

$('a-file').addEventListener('change', onAttachmentChosen);
