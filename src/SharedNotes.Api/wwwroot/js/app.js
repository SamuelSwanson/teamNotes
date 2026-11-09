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
    actions.append(el('button', { type: 'button', textContent: 'Ask a team', onclick: () => openShout(n) }));
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
  hideConflict();
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
      await showConflict(body);
    } else {
      setEditorError(e.message);
      $('save').disabled = false;
    }
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
  refreshUnread();
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
  if (changedBy && !conflict) parts.push(changedBy.deleted
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
  if (type === 'notification') {   // delivered only to the recipient
    refreshUnread();
    if ($('requests-dialog').open) renderRequests();
    if ($('notifications-dialog').open) renderNotifications();
    else showMessage(`New notification from ${data.actorName}.`);
    return;
  }
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
  for (const type of ['note-changed', 'note-deleted', 'editing', 'notification']) {
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

// ---------- Notifications ----------

async function refreshUnread() {
  try {
    const { count } = await api('GET', '/api/notifications/unread-count');
    $('unread').textContent = count > 99 ? '99+' : count;
    $('unread').classList.toggle('hidden', count === 0);
  } catch { /* the badge is a convenience */ }
}

async function renderNotifications() {
  const items = await api('GET', '/api/notifications');
  $('n-empty').classList.toggle('hidden', items.length > 0);
  $('n-list').replaceChildren(...items.map((n) => el('li', { className: n.isRead ? '' : 'unread', onclick: () => openNotification(n) },
    el('div', { className: 'msg', textContent: n.message }),
    el('div', { className: 'when', textContent: new Date(n.createdAt).toLocaleString() }))));
}

async function openNotification(n) {
  try {
    if (!n.isRead) await api('POST', `/api/notifications/${n.notificationId}/read`);
    refreshUnread();
    if (n.type === 'shout-out' || n.type === 'shout-out-resolved') {
      $('notifications-dialog').close();
      await openRequests(n.type === 'shout-out' ? 'inbox' : 'sent', true);
    } else if (n.noteId) {
      $('notifications-dialog').close();
      await openEditor(n.noteId);   // fails with a clear message if the note is gone or no longer readable
    } else {
      await renderNotifications();
    }
  } catch (e) {
    showMessage(e.message, true);
  }
}

$('bell').addEventListener('click', async () => {
  try { await renderNotifications(); $('notifications-dialog').showModal(); }
  catch (e) { showMessage('Could not load notifications: ' + e.message, true); }
});
$('n-read-all').addEventListener('click', async () => {
  await api('POST', '/api/notifications/read-all').catch((e) => showMessage(e.message, true));
  refreshUnread();
  renderNotifications();
});
$('n-close').addEventListener('click', () => $('notifications-dialog').close());

// ---------- Conflict resolution ----------
// When a save is rejected (409), show the user's unsaved version next to the latest saved one and let them choose.

let conflict = null;   // { mine: request body, latest: note as currently saved }

async function showConflict(mine) {
  let latest;
  try {
    latest = await api('GET', `/api/notes/${state.editing.noteId}`);
  } catch (e) {
    setEditorError(e.status === 404 ? 'This note was deleted by someone else.' : 'You can no longer access this note.');
    $('save').disabled = true;
    return;
  }
  conflict = { mine, latest };

  const teamName = (id) => state.teams.find((t) => t.teamId === id)?.name ?? '';
  const where = (visibility, team) => (visibility === 'Team' ? `Team: ${team}` : visibility);
  const mineTopics = state.topics.filter((t) => mine.topicIds.includes(t.topicId)).map((t) => t.name).sort().join(', ') || '—';
  const latestTopics = latest.topics.map((t) => t.name).sort().join(', ') || '—';
  const rows = [
    ['Title', mine.title, latest.title],
    ['Content', mine.content, latest.content],
    ['Visibility', where(mine.visibility, teamName(mine.teamId)), where(latest.visibility, latest.teamName)],
    ['Topics', mineTopics, latestTopics],
  ];

  setEditorError('');
  $('conflict-by').textContent = `${latest.updatedByName} saved a newer version at ${new Date(latest.updatedAt).toLocaleString()}. Differences are highlighted.`;
  $('conflict-rows').replaceChildren(...rows.map(([field, a, b]) => el('tr', { className: a === b ? '' : 'differs' },
    el('th', { scope: 'row', textContent: field }), el('td', { textContent: a }), el('td', { textContent: b }))));
  $('conflict').classList.remove('hidden');
  $('save').disabled = true;   // saving is only possible through one of the two explicit choices below
  $('reload-note').classList.add('hidden');
  $('use-latest').focus();
}

function hideConflict() {
  conflict = null;
  $('conflict').classList.add('hidden');
}

$('use-latest').addEventListener('click', () => {
  populateEditor(conflict.latest);   // replaces the form with the latest saved version
  showMessage('Loaded the latest version.');
});

$('keep-mine').addEventListener('click', () => {
  if (!confirm(`This will overwrite ${conflict.latest.updatedByName}'s changes with yours. Continue?`)) return;
  state.editing = conflict.latest;   // adopt the latest version token; the form still holds my edits
  hideConflict();
  $('save').disabled = false;
  $('note-form').requestSubmit();    // an ordinary save: still rejected if someone changes the note yet again
});

// ---------- Team requests (shout-outs) ----------

let requestsBox = 'inbox';

async function openRequests(box, includeResolved = false) {
  requestsBox = box;
  $('rq-resolved').checked = includeResolved;
  try {
    await renderRequests();
    if (!$('requests-dialog').open) $('requests-dialog').showModal();
  } catch (e) {
    showMessage('Could not load requests: ' + e.message, true);
  }
}

async function renderRequests() {
  $('rq-tab-inbox').setAttribute('aria-pressed', requestsBox === 'inbox');
  $('rq-tab-sent').setAttribute('aria-pressed', requestsBox === 'sent');
  const status = $('rq-resolved').checked ? 'all' : 'open';
  const items = await api('GET', `/api/shoutouts?box=${requestsBox}&status=${status}`);
  $('rq-empty').classList.toggle('hidden', items.length > 0);
  $('rq-list').replaceChildren(...items.map(renderRequest));
}

function renderRequest(s) {
  const open = s.status === 'Open';
  const who = requestsBox === 'inbox' ? `From ${s.senderName} to ${s.teamName}` : `To ${s.teamName}`;
  const li = el('li', { className: 'request' + (open ? '' : ' done') },
    el('div', { className: 'meta' },
      el('span', { className: 'tag', textContent: s.kind }),
      el('span', { textContent: who }),
      el('span', { textContent: new Date(s.createdAt).toLocaleString() }),
      ...(open ? [] : [el('span', { className: 'tag vis', textContent: 'Resolved' })])),
    el('p', { textContent: s.message }));

  if (s.noteId) {
    li.append(el('button', { type: 'button', textContent: s.noteTitle ? `Open note: ${s.noteTitle}` : 'Open note',
      onclick: async () => { $('requests-dialog').close(); await openEditor(s.noteId); } }));
  }
  if (!open) {
    li.append(el('p', { className: 'hint', textContent: `Resolved by ${s.resolvedByName}` + (s.response ? `: ${s.response}` : '') }));
  } else {
    const response = el('input', { type: 'text', maxLength: 500, placeholder: 'Optional reply…', 'aria-label': 'Reply (optional)' });
    li.append(el('div', { className: 'row' }, response,
      el('button', { type: 'button', className: 'primary', textContent: 'Resolve', onclick: () => resolveRequest(s, response.value) })));
  }
  return li;
}

async function resolveRequest(s, response) {
  try {
    await api('POST', `/api/shoutouts/${s.shoutOutId}/resolve`, { response: response.trim() || null });
    showMessage('Request resolved.');
    await renderRequests();
  } catch (e) {
    showMessage(e.message, true);   // e.g. someone else resolved it first (409)
    renderRequests();
  }
}

let shoutNoteId = null;

function openShout(note) {
  shoutNoteId = note?.noteId ?? null;
  $('shout-form').reset();
  fillSelect($('s-team'), null, state.teams, 'teamId', 'name');
  $('shout-note').textContent = note ? `About the note: “${note.title}”` : '';
  $('shout-note').classList.toggle('hidden', !note);
  $('shout-error').classList.add('hidden');
  $('shout-dialog').showModal();
}

$('shout-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  $('s-send').disabled = true;
  try {
    await api('POST', `/api/teams/${$('s-team').value}/shoutouts`,
      { kind: $('s-kind').value, message: $('s-message').value.trim(), noteId: shoutNoteId });
    $('shout-dialog').close();
    showMessage('Request sent to the team.');
    if ($('requests-dialog').open) renderRequests();
  } catch (e) {
    $('shout-error').textContent = e.message;
    $('shout-error').classList.remove('hidden');
  } finally {
    $('s-send').disabled = false;
  }
});

$('requests-btn').addEventListener('click', () => openRequests('inbox'));
$('rq-tab-inbox').addEventListener('click', () => { requestsBox = 'inbox'; renderRequests(); });
$('rq-tab-sent').addEventListener('click', () => { requestsBox = 'sent'; renderRequests(); });
$('rq-resolved').addEventListener('change', renderRequests);
$('rq-new').addEventListener('click', () => openShout(null));
$('rq-close').addEventListener('click', () => $('requests-dialog').close());
$('s-cancel').addEventListener('click', () => $('shout-dialog').close());
