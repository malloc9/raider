/**
 * Real GamePort implementation that communicates with the BepInEx plugin via HTTP.
 */

import type { GamePort, ChoreResult, ChoreId } from '../shared/types';

const PLUGIN_BASE_URL = 'http://localhost:51837/raider/';

/** Maps TypeScript ChoreId to plugin chore type strings. */
function mapChoreIdToPluginType(choreId: ChoreId): string {
  const mapping: Record<ChoreId, string> = {
    'claim-playtime-rewards': 'ClaimPlaytimeRewards',
    'claim-quests': 'ClaimQuests',
    'collect-inbox': 'CollectInbox',
    'buy-free-market-items': 'BuyFreeMarketItems',
    'collect-gem-mine': 'CollectGemMine',
    'clan-check-in': 'ClanCheckIn'
  };
  return mapping[choreId];
}

/** Maps plugin response to ChoreResult. */
function mapPluginResponse(choreId: ChoreId, pluginResponse: any): ChoreResult {
  if (pluginResponse.success) {
    const data = pluginResponse.data;
    let message = 'Completed successfully';
    
    if (data?.outcome === 'NothingToDo') {
      message = `Nothing to do: ${data.reason}`;
    } else if (data?.outcome === 'Success') {
      if (data.claimed?.length > 0) {
        message = `Claimed ${data.claimed.length} reward(s)`;
      } else if (data.collected?.length > 0) {
        message = `Collected ${data.collected.length} item(s)`;
      } else if (data.taken?.length > 0) {
        message = `Took ${data.taken.length} free item(s)`;
      } else if (data.rewards) {
        message = `Rewards: ${JSON.stringify(data.rewards)}`;
      } else {
        message = 'Success';
      }
    }
    
    return {
      choreId,
      success: true,
      message,
      timestamp: new Date()
    };
  } else {
    return {
      choreId,
      success: false,
      message: pluginResponse.errorMessage || 'Unknown error',
      timestamp: new Date()
    };
  }
}

export class RealGamePort implements GamePort {
  private readonly httpClient: typeof fetch;
  
  constructor() {
    this.httpClient = fetch;
  }

  async switchAccount(accountId: string): Promise<void> {
    // Account switching is handled by the desktop app via Plarium Play automation
    // The plugin just needs to be aware of the current account
    console.log(`[RealGamePort] Switching to account: ${accountId}`);
    // Could notify plugin of account switch if needed
  }

  async executeChore(choreId: ChoreId): Promise<ChoreResult> {
    const pluginChoreType = mapChoreIdToPluginType(choreId);
    console.log(`[RealGamePort] Executing chore: ${choreId} -> ${pluginChoreType}`);
    
    try {
      const response = await this.httpClient(`${PLUGIN_BASE_URL}execute-chore`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          choreType: pluginChoreType,
          parameters: {}
        }),
        signal: AbortSignal.timeout(60000)
      });
      
      if (!response.ok) {
        const errorText = await response.text();
        return {
          choreId,
          success: false,
          message: `Plugin returned ${response.status}: ${errorText}`,
          timestamp: new Date()
        };
      }
      
      const pluginResponse = await response.json();
      return mapPluginResponse(choreId, pluginResponse);
    } catch (error) {
      if (error instanceof Error) {
        if (error.name === 'TimeoutError' || error.name === 'AbortError') {
          return {
            choreId,
            success: false,
            message: 'Plugin request timed out',
            timestamp: new Date()
          };
        }
        return {
          choreId,
          success: false,
          message: `Failed to communicate with plugin: ${error.message}`,
          timestamp: new Date()
        };
      }
      return {
        choreId,
        success: false,
        message: 'Unexpected error',
        timestamp: new Date()
      };
    }
  }

  async isGameRunning(): Promise<boolean> {
    try {
      const response = await this.httpClient(`${PLUGIN_BASE_URL}health`, {
        signal: AbortSignal.timeout(5000)
      });
      return response.ok;
    } catch {
      return false;
    }
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