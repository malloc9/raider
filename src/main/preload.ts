/**
 * Preload script - exposes safe IPC APIs to renderer.
 */

import { contextBridge, ipcRenderer } from 'electron';
import type { Account, Routine, RoutineChore, ChoreId, Run } from '../shared/types';
import type { InstallCheckResult, InstallOperationResult } from '../shared/types';
import { IPC_CHANNELS, type AccountCreateRequest, type AccountDeleteRequest, type AccountRenameRequest, type RoutineGetRequest, type RoutineUpdateRequest, type RoutineReorderRequest, type RoutineToggleChoreRequest, type RunStartRequest, type RunProgressEvent, type RunCompleteEvent, type InstallerSetGamePathRequest } from './ipc';

contextBridge.exposeInMainWorld('raiderAPI', {
  // Accounts
  accounts: {
    list: (): Promise<Account[]> => ipcRenderer.invoke(IPC_CHANNELS['accounts:list']),
    create: (name: string): Promise<Account> => ipcRenderer.invoke(IPC_CHANNELS['accounts:create'], { name } as AccountCreateRequest),
    delete: (accountId: string): Promise<void> => ipcRenderer.invoke(IPC_CHANNELS['accounts:delete'], { accountId } as AccountDeleteRequest),
    rename: (accountId: string, name: string): Promise<void> => ipcRenderer.invoke(IPC_CHANNELS['accounts:rename'], { accountId, name } as AccountRenameRequest)
  },

  // Routine
  routine: {
    get: (accountId: string): Promise<Routine | null> => ipcRenderer.invoke(IPC_CHANNELS['routine:get'], { accountId } as RoutineGetRequest),
    update: (accountId: string, routine: Routine): Promise<void> => ipcRenderer.invoke(IPC_CHANNELS['routine:update'], { accountId, routine } as RoutineUpdateRequest),
    reorder: (accountId: string, choreId: ChoreId, newOrder: number): Promise<void> => ipcRenderer.invoke(IPC_CHANNELS['routine:reorder'], { accountId, choreId, newOrder } as RoutineReorderRequest),
    toggleChore: (accountId: string, choreId: ChoreId, enabled: boolean): Promise<void> => ipcRenderer.invoke(IPC_CHANNELS['routine:toggle-chore'], { accountId, choreId, enabled } as RoutineToggleChoreRequest)
  },

  // Runs
  run: {
    start: (accountId: string): Promise<{ runId: string }> => ipcRenderer.invoke(IPC_CHANNELS['run:start'], { accountId } as RunStartRequest),
    cancel: (runId: string): Promise<void> => ipcRenderer.invoke(IPC_CHANNELS['run:cancel'], { runId }),
    onProgress: (callback: (event: RunProgressEvent) => void) => {
      ipcRenderer.on(IPC_CHANNELS['run:progress'], (_e, event) => callback(event));
      return () => ipcRenderer.removeListener(IPC_CHANNELS['run:progress'], (_e, event) => callback(event));
    },
    onComplete: (callback: (event: RunCompleteEvent) => void) => {
      ipcRenderer.on(IPC_CHANNELS['run:complete'], (_e, event) => callback(event));
      return () => ipcRenderer.removeListener(IPC_CHANNELS['run:complete'], (_e, event) => callback(event));
    }
  },

  // Installer
  installer: {
    check: (): Promise<InstallCheckResult> => ipcRenderer.invoke(IPC_CHANNELS['installer:check']),
    install: (): Promise<InstallOperationResult> => ipcRenderer.invoke(IPC_CHANNELS['installer:install']),
    remove: (): Promise<InstallOperationResult> => ipcRenderer.invoke(IPC_CHANNELS['installer:remove']),
    getGamePath: (): Promise<string | null> => ipcRenderer.invoke(IPC_CHANNELS['installer:get-game-path']),
    setGamePath: (gamePath: string): Promise<void> => ipcRenderer.invoke(IPC_CHANNELS['installer:set-game-path'], { gamePath } as InstallerSetGamePathRequest)
  }
});

export type RaiderAPI = typeof contextBridge extends { exposeInMainWorld: (name: string, api: infer T) => void } ? T : never;