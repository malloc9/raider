/**
 * Settings persistence using electron-store.
 * Stores data in the user's roaming app-data folder under Raider.
 */

import { app } from 'electron';
import Store from 'electron-store';
import type { Routine, Account, SettingsPort } from '../shared/types';

const store = new Store({
  name: 'raider-settings',
  cwd: app.getPath('appData'), // Roaming app-data folder
  schema: {
    accounts: {
      type: 'array',
      default: [],
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          name: { type: 'string' },
          routine: {
            type: 'object',
            properties: {
              chores: {
                type: 'array',
                items: {
                  type: 'object',
                  properties: {
                    id: { type: 'string' },
                    enabled: { type: 'boolean' },
                    order: { type: 'number' }
                  },
                  required: ['id', 'enabled', 'order']
                }
              }
            },
            required: ['chores']
          }
        },
        required: ['id', 'name', 'routine']
      }
    }
  }
});

type StoredAccount = Account;
type StoredRoutine = Routine;

function getAccounts(): StoredAccount[] {
  return store.get('accounts', []) as StoredAccount[];
}

function setAccounts(accounts: StoredAccount[]): void {
  store.set('accounts', accounts);
}

export const settingsPort: SettingsPort = {
  async loadRoutine(accountId: string): Promise<Routine | null> {
    const accounts = getAccounts();
    const account = accounts.find(a => a.id === accountId);
    return account?.routine ?? null;
  },

  async saveRoutine(accountId: string, routine: Routine): Promise<void> {
    const accounts = getAccounts();
    const index = accounts.findIndex(a => a.id === accountId);
    if (index >= 0) {
      accounts[index] = { ...accounts[index], routine };
      setAccounts(accounts);
    }
  },

  async loadAccounts(): Promise<Account[]> {
    return getAccounts();
  },

  async saveAccounts(accounts: Account[]): Promise<void> {
    setAccounts(accounts);
  }
};

/**
 * Reset all settings (useful for testing).
 */
export function resetSettings(): void {
  store.clear();
}