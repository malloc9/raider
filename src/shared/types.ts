/**
 * Core domain types for Raider.
 * Vocabulary from GLOSSARY.md
 */

/**
 * One self-contained piece of routine in-game work.
 */
export type ChoreId =
  | 'claim-playtime-rewards'
  | 'claim-quests'
  | 'collect-inbox'
  | 'buy-free-market-items'
  | 'collect-gem-mine'
  | 'clan-check-in';

export interface ChoreDefinition {
  id: ChoreId;
  name: string;
  description: string;
}

/**
 * All six v1 Chores in their default order.
 */
export const V1_CHORES: readonly ChoreDefinition[] = [
  {
    id: 'claim-playtime-rewards',
    name: 'Claim playtime rewards',
    description: 'Collect the timed playtime gifts whose timers have already finished.'
  },
  {
    id: 'claim-quests',
    name: 'Claim quests',
    description: 'Collect every completed quest reward on the Daily, Weekly, Monthly and Advanced tabs, plus the Daily and Weekly progress chests.'
  },
  {
    id: 'collect-inbox',
    name: 'Collect inbox',
    description: 'Take every inbox item that needs no choice; choice packs are left alone.'
  },
  {
    id: 'buy-free-market-items',
    name: 'Buy free Market items',
    description: 'Take Market items whose price is zero; nothing that costs silver or gems.'
  },
  {
    id: 'collect-gem-mine',
    name: 'Collect Gem Mine',
    description: 'Take the gems accumulated in the Gem Mine.'
  },
  {
    id: 'clan-check-in',
    name: 'Clan check-in',
    description: 'Perform the daily Clan check-in.'
  }
] as const;

/**
 * A Chore in a Routine - includes enabled state and order.
 */
export interface RoutineChore {
  id: ChoreId;
  enabled: boolean;
  order: number;
}

/**
 * The ordered list of Chores configured for one Account.
 */
export interface Routine {
  chores: RoutineChore[];
}

/**
 * One Raid: Shadow Legends player profile.
 */
export interface Account {
  id: string;
  name: string;
  routine: Routine;
}

/**
 * Result of executing a single Chore.
 */
export interface ChoreResult {
  choreId: ChoreId;
  success: boolean;
  message?: string;
  timestamp: Date;
}

/**
 * One execution of an Account's Routine.
 */
export interface Run {
  accountId: string;
  startedAt: Date;
  finishedAt?: Date;
  results: ChoreResult[];
}

/**
 * Port interface for Game interactions (for testing with fakes).
 */
export interface GamePort {
  switchAccount(accountId: string): Promise<void>;
  executeChore(choreId: ChoreId): Promise<ChoreResult>;
  isGameRunning(): Promise<boolean>;
  launchGame(): Promise<void>;
}

/**
 * Port interface for Settings persistence.
 */
export interface SettingsPort {
  loadRoutine(accountId: string): Promise<Routine | null>;
  saveRoutine(accountId: string, routine: Routine): Promise<void>;
  loadAccounts(): Promise<Account[]>;
  saveAccounts(accounts: Account[]): Promise<void>;
}

/**
 * Installation status of BepInEx and Raider plugin.
 */
export type InstallStatus = 
  | 'not-installed'
  | 'installed'
  | 'needs-repair'
  | 'incompatible-bepinex'
  | 'unknown';

/**
 * Result of an installation check.
 */
export interface InstallCheckResult {
  status: InstallStatus;
  message?: string;
  details?: {
    bepinexPresent: boolean;
    bepinexOwned: boolean;
    bepinexCompatible: boolean;
    pluginPresent: boolean;
    pluginOwned: boolean;
    manifestPresent: boolean;
    foreignPlugins: string[];
    disableFolders: string[];
  };
}

/**
 * Install manifest recording what Raider installed.
 */
export interface InstallManifest {
  version: number;
  installedAt: string; // ISO timestamp
  bepinexOwned: boolean;
  files: string[]; // Relative paths from game folder
  folders: string[]; // Relative paths from game folder
}

/**
 * Result of an install/repair/remove operation.
 */
export interface InstallOperationResult {
  success: boolean;
  message?: string;
  error?: string;
}

/**
 * Port interface for Game installation management.
 */
export interface InstallerPort {
  /** Check installation status */
  checkInstallation(gamePath: string): Promise<InstallCheckResult>;
  /** Install or repair Raider in the game folder */
  install(gamePath: string): Promise<InstallOperationResult>;
  /** Remove Raider from the game folder */
  remove(gamePath: string): Promise<InstallOperationResult>;
  /** Get the game folder path */
  getGamePath(): Promise<string | null>;
  /** Set the game folder path */
  setGamePath(path: string): Promise<void>;
}

/**
 * Default routine with all six v1 chores enabled in default order.
 */
export function createDefaultRoutine(): Routine {
  return {
    chores: V1_CHORES.map((chore, index) => ({
      id: chore.id,
      enabled: true,
      order: index
    }))
  };
}

/**
 * Create a new account with default routine.
 */
export function createAccount(id: string, name: string): Account {
  return {
    id,
    name,
    routine: createDefaultRoutine()
  };
}