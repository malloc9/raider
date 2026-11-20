/**
 * Renderer process - UI logic for Routine management.
 */

import type { Account, Routine, RoutineChore, ChoreId, Run, ChoreResult } from '../shared/types';
import { V1_CHORES } from '../shared/types';

// Type for the exposed API
interface RaiderAPI {
  accounts: {
    list: () => Promise<Account[]>;
    create: (name: string) => Promise<Account>;
    delete: (accountId: string) => Promise<void>;
    rename: (accountId: string, name: string) => Promise<void>;
  };
  routine: {
    get: (accountId: string) => Promise<Routine | null>;
    update: (accountId: string, routine: Routine) => Promise<void>;
    reorder: (accountId: string, choreId: ChoreId, newOrder: number) => Promise<void>;
    toggleChore: (accountId: string, choreId: ChoreId, enabled: boolean) => Promise<void>;
  };
  run: {
    start: (accountId: string) => Promise<{ runId: string }>;
    chore: (accountId: string, choreId: ChoreId) => Promise<{ runId: string }>;
    cancel: (runId: string) => Promise<void>;
    onProgress: (callback: (event: any) => void) => () => void;
    onComplete: (callback: (event: any) => void) => void;
  };
}

declare global {
  interface Window {
    raiderAPI: RaiderAPI;
  }
}

// State
let accounts: Account[] = [];
let selectedAccountId: string | null = null;
let currentRoutine: Routine | null = null;
let currentRunId: string | null = null;
let runResults: ChoreResult[] = [];
let isRunning = false;

// DOM Elements
const accountList = document.getElementById('account-list') as HTMLUListElement;
const addAccountBtn = document.getElementById('add-account-btn') as HTMLButtonElement;
const newAccountBtn = document.getElementById('new-account-btn') as HTMLButtonElement;
const routineAccountName = document.getElementById('routine-account-name') as HTMLHeadingElement;
const routineAccountHint = document.getElementById('routine-account-hint') as HTMLParagraphElement;
const choreList = document.getElementById('chore-list') as HTMLUListElement;
const runStatus = document.getElementById('run-status') as HTMLDivElement;
const statusValue = document.getElementById('status-value') as HTMLDivElement;
const runResultsList = document.getElementById('run-results') as HTMLUListElement;
const runBtn = document.getElementById('run-btn') as HTMLButtonElement;

// Modals
const accountModal = document.getElementById('account-modal') as HTMLDivElement;
const modalTitle = document.getElementById('modal-title') as HTMLHeadingElement;
const modalInput = document.getElementById('modal-input') as HTMLInputElement;
const modalCancel = document.getElementById('modal-cancel') as HTMLButtonElement;
const modalConfirm = document.getElementById('modal-confirm') as HTMLButtonElement;

const renameModal = document.getElementById('rename-modal') as HTMLDivElement;
const renameInput = document.getElementById('rename-input') as HTMLInputElement;
const renameCancel = document.getElementById('rename-cancel') as HTMLButtonElement;
const renameConfirm = document.getElementById('rename-confirm') as HTMLButtonElement;

let modalMode: 'create' | 'rename' = 'create';
let modalAccountId: string | null = null;

// Initialize
async function init() {
  await loadAccounts();
  setupEventListeners();
  setupDragAndDrop();
  setupRunListeners();
}

async function loadAccounts() {
  accounts = await window.raiderAPI.accounts.list();
  renderAccountList();
}

function renderAccountList() {
  accountList.innerHTML = '';
  if (accounts.length === 0) {
    accountList.innerHTML = '<li style="padding: 1rem; color: #666; text-align: center;">No accounts yet</li>';
    return;
  }

  for (const account of accounts) {
    const li = document.createElement('li');
    li.className = 'account-item' + (account.id === selectedAccountId ? ' active' : '');
    li.dataset.accountId = account.id;
    li.innerHTML = `
      <span class="account-name" title="${escapeHtml(account.name)}">${escapeHtml(account.name)}</span>
      <div class="account-actions">
        <button class="btn-icon rename-btn" title="Rename" data-account-id="${account.id}">✏️</button>
        <button class="btn-icon delete delete-btn" title="Delete" data-account-id="${account.id}">🗑️</button>
      </div>
    `;
    accountList.appendChild(li);
  }
}

async function selectAccount(accountId: string) {
  selectedAccountId = accountId;
  renderAccountList();
  await loadRoutine(accountId);
}

async function loadRoutine(accountId: string) {
  currentRoutine = await window.raiderAPI.routine.get(accountId);
  const account = accounts.find(a => a.id === accountId);
  if (account) {
    routineAccountName.textContent = account.name;
    routineAccountHint.textContent = 'Drag to reorder, click checkbox to toggle';
  }
  renderChoreList();
}

function renderChoreList() {
  choreList.innerHTML = '';

  if (!currentRoutine) {
    choreList.innerHTML = '<li class="empty-state"><div class="empty-state-icon">📋</div><p>No routine loaded</p></li>';
    return;
  }

  // Sort by order
  const sortedChores = [...currentRoutine.chores].sort((a, b) => a.order - b.order);

  if (sortedChores.length === 0) {
    choreList.innerHTML = '<li class="empty-state"><div class="empty-state-icon">➕</div><p>No chores in routine</p></li>';
    return;
  }

  for (const routineChore of sortedChores) {
    const def = V1_CHORES.find(c => c.id === routineChore.id);
    if (!def) continue;

    const li = document.createElement('li');
    li.className = `chore-item${!routineChore.enabled ? ' disabled' : ''}`;
    li.draggable = true;
    li.dataset.choreId = routineChore.id;
    li.dataset.order = routineChore.order.toString();
    li.innerHTML = `
      <span class="drag-handle" title="Drag to reorder">⋮⋮</span>
      <input type="checkbox" class="chore-checkbox" ${routineChore.enabled ? 'checked' : ''} data-chore-id="${routineChore.id}">
      <div class="chore-info">
        <div class="chore-name">${escapeHtml(def.name)}</div>
        <div class="chore-description">${escapeHtml(def.description)}</div>
      </div>
      <span class="chore-order">${routineChore.order + 1}</span>
      <button class="btn-icon run-chore-btn" title="Run this chore" data-chore-id="${routineChore.id}" ${!routineChore.enabled ? 'disabled' : ''}>▶</button>
    `;
    choreList.appendChild(li);
  }

  updateRunButtonState();
}

function updateRunButtonState() {
  const hasEnabledChores = currentRoutine?.chores.some(c => c.enabled) ?? false;
  runBtn.disabled = !selectedAccountId || !hasEnabledChores || isRunning;
  runBtn.textContent = isRunning ? 'Running...' : 'Run Routine';

  // Enable/disable individual chore run buttons
  const choreRunButtons = choreList.querySelectorAll('.run-chore-btn') as NodeListOf<HTMLButtonElement>;
  for (const btn of choreRunButtons) {
    const choreId = btn.dataset.choreId;
    const chore = currentRoutine?.chores.find(c => c.id === choreId);
    btn.disabled = !selectedAccountId || !chore?.enabled || isRunning;
  }
}

// Event Listeners
function setupEventListeners() {
  // Account list clicks
  accountList.addEventListener('click', async (e) => {
    const target = e.target as HTMLElement;

    // Select account
    const accountItem = target.closest('.account-item');
    if (accountItem && !target.closest('.btn-icon')) {
      const accountId = accountItem.dataset.accountId;
      if (accountId) await selectAccount(accountId);
      return;
    }

    // Rename button
    if (target.closest('.rename-btn')) {
      const btn = target.closest('.rename-btn') as HTMLButtonElement;
      const accountId = btn.dataset.accountId!;
      const account = accounts.find(a => a.id === accountId);
      if (account) openRenameModal(account);
      return;
    }

    // Delete button
    if (target.closest('.delete-btn')) {
      const btn = target.closest('.delete-btn') as HTMLButtonElement;
      const accountId = btn.dataset.accountId!;
      if (confirm('Delete this account?')) {
        await window.raiderAPI.accounts.delete(accountId);
        if (selectedAccountId === accountId) {
          selectedAccountId = null;
          currentRoutine = null;
          routineAccountName.textContent = 'Select an account';
          routineAccountHint.textContent = 'Choose an account from the left to manage its Routine';
          renderChoreList();
        }
        await loadAccounts();
      }
      return;
    }
  });

  // New account buttons
  newAccountBtn.addEventListener('click', () => openCreateModal());
  addAccountBtn.addEventListener('click', () => openCreateModal());

  // Modal handlers
  modalCancel.addEventListener('click', closeModals);
  renameCancel.addEventListener('click', closeModals);

  modalConfirm.addEventListener('click', async () => {
    const name = modalInput.value.trim();
    if (!name) return;

    if (modalMode === 'create') {
      const account = await window.raiderAPI.accounts.create(name);
      await loadAccounts();
      await selectAccount(account.id);
    }
    closeModals();
  });

  renameConfirm.addEventListener('click', async () => {
    const name = renameInput.value.trim();
    if (!name || !modalAccountId) return;

    await window.raiderAPI.accounts.rename(modalAccountId, name);
    await loadAccounts();
    if (selectedAccountId === modalAccountId) {
      await selectAccount(modalAccountId);
    }
    closeModals();
  });

  // Close modals on overlay click
  accountModal.addEventListener('click', (e) => {
    if (e.target === accountModal) closeModals();
  });
  renameModal.addEventListener('click', (e) => {
    if (e.target === renameModal) closeModals();
  });

  // Chore list events (checkbox toggle)
  choreList.addEventListener('change', async (e) => {
    const target = e.target as HTMLInputElement;
    if (target.matches('.chore-checkbox') && selectedAccountId) {
      const choreId = target.dataset.choreId as ChoreId;
      const enabled = target.checked;
      await window.raiderAPI.routine.toggleChore(selectedAccountId, choreId, enabled);
      await loadRoutine(selectedAccountId);
    }
  });

  // Run single chore button
  choreList.addEventListener('click', async (e) => {
    const target = e.target as HTMLElement;
    const btn = target.closest('.run-chore-btn') as HTMLButtonElement;
    if (btn && selectedAccountId && !isRunning) {
      const choreId = btn.dataset.choreId as ChoreId;
      isRunning = true;
      updateRunButtonState();
      runResults = [];
      runResultsList.innerHTML = '';
      runStatus.className = 'run-status running';
      statusValue.textContent = `Running ${getChoreName(choreId)}...`;

      const { runId } = await window.raiderAPI.run.chore(selectedAccountId, choreId);
      currentRunId = runId;
    }
  });

  // Run button
  runBtn.addEventListener('click', async () => {
    if (!selectedAccountId || isRunning) return;
    isRunning = true;
    updateRunButtonState();
    runResults = [];
    runResultsList.innerHTML = '';
    runStatus.className = 'run-status running';
    statusValue.textContent = 'Running...';

    const { runId } = await window.raiderAPI.run.start(selectedAccountId);
    currentRunId = runId;
  });
}

function openCreateModal() {
  modalMode = 'create';
  modalTitle.textContent = 'New Account';
  modalInput.value = '';
  accountModal.classList.remove('hidden');
  modalInput.focus();
}

function openRenameModal(account: Account) {
  modalMode = 'rename';
  modalAccountId = account.id;
  renameInput.value = account.name;
  renameModal.classList.remove('hidden');
  renameInput.focus();
}

function closeModals() {
  accountModal.classList.add('hidden');
  renameModal.classList.add('hidden');
  modalInput.value = '';
  renameInput.value = '';
  modalAccountId = null;
}

// Drag and Drop for reordering
function setupDragAndDrop() {
  let draggedItem: HTMLLIElement | null = null;
  let dragOverItem: HTMLLIElement | null = null;

  choreList.addEventListener('dragstart', (e) => {
    const target = e.target as HTMLElement;
    const item = target.closest('.chore-item') as HTMLLIElement;
    if (item) {
      draggedItem = item;
      item.classList.add('dragging');
      e.dataTransfer!.effectAllowed = 'move';
    }
  });

  choreList.addEventListener('dragend', (e) => {
    const target = e.target as HTMLElement;
    const item = target.closest('.chore-item') as HTMLLIElement;
    if (item) {
      item.classList.remove('dragging');
    }
    draggedItem = null;
    dragOverItem = null;
    document.querySelectorAll('.chore-item.drag-over').forEach(el => el.classList.remove('drag-over'));
  });

  choreList.addEventListener('dragover', (e) => {
    e.preventDefault();
    const target = e.target as HTMLElement;
    const item = target.closest('.chore-item') as HTMLLIElement;
    if (item && item !== draggedItem) {
      dragOverItem = item;
      document.querySelectorAll('.chore-item.drag-over').forEach(el => el.classList.remove('drag-over'));
      item.classList.add('drag-over');
    }
    e.dataTransfer!.dropEffect = 'move';
  });

  choreList.addEventListener('drop', async (e) => {
    e.preventDefault();
    if (!draggedItem || !dragOverItem || !selectedAccountId) return;

    const draggedChoreId = draggedItem.dataset.choreId as ChoreId;
    const targetChoreId = dragOverItem.dataset.choreId as ChoreId;
    const targetOrder = parseInt(dragOverItem.dataset.order || '0', 10);

    if (draggedChoreId === targetChoreId) return;

    // Reorder: move dragged item to target's position
    await window.raiderAPI.routine.reorder(selectedAccountId, draggedChoreId, targetOrder);
    await loadRoutine(selectedAccountId);
  });

  choreList.addEventListener('dragleave', (e) => {
    const target = e.target as HTMLElement;
    const item = target.closest('.chore-item') as HTMLLIElement;
    if (item && !item.contains(e.relatedTarget as Node)) {
      item.classList.remove('drag-over');
    }
  });
}

// Run progress/complete listeners
function setupRunListeners() {
  window.raiderAPI.run.onProgress((event) => {
    // Could show per-chore progress here
    console.log('Progress:', event);
  });

  window.raiderAPI.run.onComplete((event) => {
    isRunning = false;
    currentRunId = null;
    updateRunButtonState();

    const run = event.run as Run;
    runResults = run.results;
    renderRunResults(run);

    if (run.results.every(r => r.success)) {
      runStatus.className = 'run-status complete';
      statusValue.textContent = 'Complete';
    } else {
      runStatus.className = 'run-status error';
      statusValue.textContent = 'Completed with errors';
    }
  });
}

function renderRunResults(run: Run) {
  runResultsList.innerHTML = '';

  for (const result of run.results) {
    const li = document.createElement('li');
    li.className = 'run-result-item';
    li.innerHTML = `
      <div class="result-icon ${result.success ? 'success' : 'failure'}">
        ${result.success ? '✓' : '✕'}
      </div>
      <div class="result-info">
        <div class="result-chore-name">${getChoreName(result.choreId)}</div>
        <div class="result-message">${escapeHtml(result.message || '')}</div>
      </div>
      <div class="result-time">${formatTime(result.timestamp)}</div>
    `;
    runResultsList.appendChild(li);
  }
}

function getChoreName(choreId: ChoreId): string {
  const def = V1_CHORES.find(c => c.id === choreId);
  return def?.name || choreId;
}

function formatTime(date: Date): string {
  return new Date(date).toLocaleTimeString();
}

function escapeHtml(text: string): string {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// Start
init();