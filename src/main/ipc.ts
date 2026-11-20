/**
 * IPC channel definitions for main/renderer communication.
 */

import type { Account, Routine, RoutineChore, ChoreId, Run } from '../shared/types';
import type { InstallCheckResult, InstallOperationResult } from '../shared/types';

export const IPC_CHANNELS = {
  // Account management
  'accounts:list': 'accounts:list',
  'accounts:create': 'accounts:create',
  'accounts:delete': 'accounts:delete',
  'accounts:rename': 'accounts:rename',

  // Routine management
  'routine:get': 'routine:get',
  'routine:update': 'routine:update',
  'routine:reorder': 'routine:reorder',
  'routine:toggle-chore': 'routine:toggle-chore',

  // Runs
  'run:start': 'run:start',
  'run:cancel': 'run:cancel',
  'run:progress': 'run:progress',
  'run:complete': 'run:complete',

  // Installer
  'installer:check': 'installer:check',
  'installer:install': 'installer:install',
  'installer:remove': 'installer:remove',
  'installer:get-game-path': 'installer:get-game-path',
  'installer:set-game-path': 'installer:set-game-path'
} as const;

// Installer types
export type InstallerCheckResponse = InstallCheckResult;
export type InstallerInstallResponse = InstallOperationResult;
export type InstallerRemoveResponse = InstallOperationResult;
export type InstallerGetGamePathResponse = string | null;
export type InstallerSetGamePathRequest = { gamePath: string };
export type InstallerSetGamePathResponse = void;

export type AccountsListResponse = Account[];
export type AccountCreateRequest = { name: string };
export type AccountCreateResponse = Account;
export type AccountDeleteRequest = { accountId: string };
export type AccountRenameRequest = { accountId: string; name: string };

export type RoutineGetRequest = { accountId: string };
export type RoutineGetResponse = Routine | null;
export type RoutineUpdateRequest = { accountId: string; routine: Routine };
export type RoutineReorderRequest = { accountId: string; choreId: ChoreId; newOrder: number };
export type RoutineToggleChoreRequest = { accountId: string; choreId: ChoreId; enabled: boolean };

export type RunStartRequest = { accountId: string };
export type RunStartResponse = { runId: string };
export type RunProgressEvent = { runId: string; choreId: ChoreId; result: any };
export type RunCompleteEvent = { runId: string; run: Run };