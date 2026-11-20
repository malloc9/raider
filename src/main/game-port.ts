/**
 * Real GamePort implementation (placeholder for actual BepInEx plugin communication).
 * This would communicate with the in-process plugin via IPC or similar.
 */

import type { GamePort, ChoreResult, ChoreId } from '../shared/types';

export class RealGamePort implements GamePort {
  async switchAccount(accountId: string): Promise<void> {
    // TODO: Implement actual account switching via BepInEx plugin
    console.log(`[RealGamePort] Switching to account: ${accountId}`);
  }

  async executeChore(choreId: ChoreId): Promise<ChoreResult> {
    // TODO: Implement actual chore execution via BepInEx plugin
    console.log(`[RealGamePort] Executing chore: ${choreId}`);
    return {
      choreId,
      success: true,
      message: 'Executed via real game port',
      timestamp: new Date()
    };
  }

  async isGameRunning(): Promise<boolean> {
    // TODO: Check if Plarium Play / game is running
    return false;
  }

  async launchGame(): Promise<void> {
    // TODO: Launch Plarium Play
    console.log('[RealGamePort] Launching game');
  }
}

/**
 * Fake GamePort for testing.
 * Records all calls for verification in tests.
 */

export interface FakeGamePortState {
  switchedAccounts: string[];
  executedChores: ChoreId[];
  gameRunning: boolean;
  launchGameCalls: number;
  choreResults: Map<ChoreId, ChoreResult>;
}

export function createFakeGamePort(initialState?: Partial<FakeGamePortState>): GamePort & { state: FakeGamePortState; setChoreResult: (choreId: ChoreId, result: ChoreResult) => void } {
  const state: FakeGamePortState = {
    switchedAccounts: [],
    executedChores: [],
    gameRunning: false,
    launchGameCalls: 0,
    choreResults: new Map(),
    ...initialState
  };

  const port: GamePort = {
    async switchAccount(accountId: string): Promise<void> {
      state.switchedAccounts.push(accountId);
    },

    async executeChore(choreId: ChoreId): Promise<ChoreResult> {
      state.executedChores.push(choreId);
      const customResult = state.choreResults.get(choreId);
      if (customResult) {
        return customResult;
      }
      return {
        choreId,
        success: true,
        message: 'Executed via fake game port',
        timestamp: new Date()
      };
    },

    async isGameRunning(): Promise<boolean> {
      return state.gameRunning;
    },

    async launchGame(): Promise<void> {
      state.launchGameCalls++;
      state.gameRunning = true;
    }
  };

  return {
    ...port,
    state,
    setChoreResult(choreId: ChoreId, result: ChoreResult) {
      state.choreResults.set(choreId, result);
    }
  };
}