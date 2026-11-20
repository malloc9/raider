/**
 * Main process entry point.
 */

import { app, BrowserWindow, ipcMain, dialog } from 'electron';
import path from 'path';
import { settingsPort } from './settings';
import { runRoutine, type OrchestratorDeps } from './orchestrator';
import { createFakeGamePort, RealGamePort } from './game-port';
import { IPC_CHANNELS, type RoutineGetRequest, type RoutineUpdateRequest, type RoutineReorderRequest, type RoutineToggleChoreRequest, type AccountCreateRequest, type AccountDeleteRequest, type AccountRenameRequest, type RunStartRequest, type RunChoreRequest } from './ipc';
import type { Account, Routine, ChoreId, Run } from '../shared/types';
import { createAccount, createDefaultRoutine } from '../shared/types';

let mainWindow: BrowserWindow | null = null;
const runningRuns = new Map<string, { account: Account; abortController: AbortController }>();

// Use fake port for development, real port for production
const isDev = process.env.NODE_ENV === 'development';
const gamePort = isDev ? createFakeGamePort() : new RealGamePort();
const orchestratorDeps: OrchestratorDeps = { gamePort };

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1000,
    height: 700,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  if (isDev) {
    mainWindow.loadFile(path.join(__dirname, '../renderer/index.html'));
    mainWindow.webContents.openDevTools();
  } else {
    mainWindow.loadFile(path.join(__dirname, '../renderer/index.html'));
  }

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

app.whenReady().then(() => {
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// IPC Handlers

// Accounts
ipcMain.handle(IPC_CHANNELS['accounts:list'], async (): Promise<Account[]> => {
  return settingsPort.loadAccounts();
});

ipcMain.handle(IPC_CHANNELS['accounts:create'], async (_event, request: AccountCreateRequest): Promise<Account> => {
  const accounts = await settingsPort.loadAccounts();
  const id = `account-${Date.now()}`;
  const account = createAccount(id, request.name);
  accounts.push(account);
  await settingsPort.saveAccounts(accounts);
  return account;
});

ipcMain.handle(IPC_CHANNELS['accounts:delete'], async (_event, request: AccountDeleteRequest): Promise<void> => {
  const accounts = await settingsPort.loadAccounts();
  const filtered = accounts.filter(a => a.id !== request.accountId);
  await settingsPort.saveAccounts(filtered);
});

ipcMain.handle(IPC_CHANNELS['accounts:rename'], async (_event, request: AccountRenameRequest): Promise<void> => {
  const accounts = await settingsPort.loadAccounts();
  const account = accounts.find(a => a.id === request.accountId);
  if (account) {
    account.name = request.name;
    await settingsPort.saveAccounts(accounts);
  }
});

// Routine
ipcMain.handle(IPC_CHANNELS['routine:get'], async (_event, request: RoutineGetRequest): Promise<Routine | null> => {
  return settingsPort.loadRoutine(request.accountId);
});

ipcMain.handle(IPC_CHANNELS['routine:update'], async (_event, request: RoutineUpdateRequest): Promise<void> => {
  await settingsPort.saveRoutine(request.accountId, request.routine);
});

ipcMain.handle(IPC_CHANNELS['routine:reorder'], async (_event, request: RoutineReorderRequest): Promise<void> => {
  const routine = await settingsPort.loadRoutine(request.accountId);
  if (!routine) return;

  const chore = routine.chores.find(c => c.id === request.choreId);
  if (!chore) return;

  // Remove from current position
  const otherChores = routine.chores.filter(c => c.id !== request.choreId);

  // Adjust orders of other chores
  for (const c of otherChores) {
    if (c.order >= request.newOrder) c.order++;
  }

  // Insert at new position
  chore.order = request.newOrder;
  routine.chores = [...otherChores, chore].sort((a, b) => a.order - b.order);

  // Normalize orders to be sequential
  routine.chores.forEach((c, i) => { c.order = i; });

  await settingsPort.saveRoutine(request.accountId, routine);
});

ipcMain.handle(IPC_CHANNELS['routine:toggle-chore'], async (_event, request: RoutineToggleChoreRequest): Promise<void> => {
  const routine = await settingsPort.loadRoutine(request.accountId);
  if (!routine) return;

  const chore = routine.chores.find(c => c.id === request.choreId);
  if (chore) {
    chore.enabled = request.enabled;
    await settingsPort.saveRoutine(request.accountId, routine);
  }
});

// Runs
ipcMain.handle(IPC_CHANNELS['run:start'], async (_event, request: RunStartRequest): Promise<{ runId: string }> => {
  const accounts = await settingsPort.loadAccounts();
  const account = accounts.find(a => a.id === request.accountId);
  if (!account) throw new Error('Account not found');

  const runId = `run-${Date.now()}`;
  const abortController = new AbortController();

  runningRuns.set(runId, { account, abortController });

  // Run in background
  (async () => {
    try {
      const run = await runRoutine(account, orchestratorDeps);
      mainWindow?.webContents.send(IPC_CHANNELS['run:complete'], { runId, run });
    } catch (error) {
      mainWindow?.webContents.send(IPC_CHANNELS['run:complete'], {
        runId,
        run: {
          accountId: account.id,
          startedAt: new Date(),
          finishedAt: new Date(),
          results: []
        }
      });
    } finally {
      runningRuns.delete(runId);
    }
  })();

  return { runId };
});

ipcMain.handle(IPC_CHANNELS['run:chore'], async (_event, request: RunChoreRequest): Promise<{ runId: string }> => {
  const accounts = await settingsPort.loadAccounts();
  const account = accounts.find(a => a.id === request.accountId);
  if (!account) throw new Error('Account not found');

  const runId = `run-${Date.now()}`;
  const abortController = new AbortController();

  runningRuns.set(runId, { account, abortController });

  // Run single chore in background
  (async () => {
    try {
      await gamePort.switchAccount(account.id);
      const result = await gamePort.executeChore(request.choreId);
      const run: Run = {
        accountId: account.id,
        startedAt: new Date(),
        finishedAt: new Date(),
        results: [result]
      };
      mainWindow?.webContents.send(IPC_CHANNELS['run:complete'], { runId, run });
    } catch (error) {
      mainWindow?.webContents.send(IPC_CHANNELS['run:complete'], {
        runId,
        run: {
          accountId: account.id,
          startedAt: new Date(),
          finishedAt: new Date(),
          results: []
        }
      });
    } finally {
      runningRuns.delete(runId);
    }
  })();

  return { runId };
});

ipcMain.handle(IPC_CHANNELS['run:cancel'], async (_event, { runId }: { runId: string }): Promise<void> => {
  const run = runningRuns.get(runId);
  if (run) {
    run.abortController.abort();
    runningRuns.delete(runId);
  }
});

// Export for testing
export { settingsPort, gamePort, orchestratorDeps, runningRuns, createWindow };