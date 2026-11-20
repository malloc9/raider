/**
 * Orchestration tests against a fake Game port.
 * Confirms disabled Chores are skipped and saved order is used.
 */

import { runRoutine, type OrchestratorDeps } from '../main/orchestrator';
import { createFakeGamePort, type FakeGamePortState } from '../main/game-port';
import { createAccount, createDefaultRoutine, type Account, type Routine, type ChoreId, V1_CHORES } from '../shared/types';

describe('Orchestrator', () => {
  let fakePort: ReturnType<typeof createFakeGamePort>;
  let deps: OrchestratorDeps;

  beforeEach(() => {
    fakePort = createFakeGamePort();
    deps = { gamePort: fakePort };
  });

  function createTestAccount(overrides: Partial<Account> = {}): Account {
    return createAccount('test-account', 'Test Account');
  }

  describe('disabled chores are skipped', () => {
    it('executes only enabled chores', async () => {
      const account = createTestAccount();
      // Disable first and third chores
      account.routine.chores[0].enabled = false; // claim-playtime-rewards
      account.routine.chores[2].enabled = false; // collect-inbox

      await runRoutine(account, deps);

      const executedIds = fakePort.state.executedChores;
      expect(executedIds).not.toContain('claim-playtime-rewards');
      expect(executedIds).not.toContain('collect-inbox');
      expect(executedIds).toContain('claim-quests');
      expect(executedIds).toContain('buy-free-market-items');
      expect(executedIds).toContain('collect-gem-mine');
      expect(executedIds).toContain('clan-check-in');
    });

    it('executes no chores when all are disabled', async () => {
      const account = createTestAccount();
      account.routine.chores.forEach(c => c.enabled = false);

      const run = await runRoutine(account, deps);

      expect(fakePort.state.executedChores).toHaveLength(0);
      expect(run.results).toHaveLength(0);
    });

    it('executes all chores when all are enabled', async () => {
      const account = createTestAccount();
      // All enabled by default

      await runRoutine(account, deps);

      expect(fakePort.state.executedChores).toHaveLength(6);
      expect(fakePort.state.executedChores).toEqual([
        'claim-playtime-rewards',
        'claim-quests',
        'collect-inbox',
        'buy-free-market-items',
        'collect-gem-mine',
        'clan-check-in'
      ]);
    });
  });

  describe('saved order is used', () => {
    it('executes chores in custom order', async () => {
      const account = createTestAccount();
      // Reorder: Gem Mine first, then Clan check-in, then the rest
      const routine = account.routine;
      routine.chores = [
        { id: 'collect-gem-mine', enabled: true, order: 0 },
        { id: 'clan-check-in', enabled: true, order: 1 },
        { id: 'claim-playtime-rewards', enabled: true, order: 2 },
        { id: 'claim-quests', enabled: true, order: 3 },
        { id: 'collect-inbox', enabled: true, order: 4 },
        { id: 'buy-free-market-items', enabled: true, order: 5 }
      ];

      await runRoutine(account, deps);

      expect(fakePort.state.executedChores).toEqual([
        'collect-gem-mine',
        'clan-check-in',
        'claim-playtime-rewards',
        'claim-quests',
        'collect-inbox',
        'buy-free-market-items'
      ]);
    });

    it('respects order with some chores disabled', async () => {
      const account = createTestAccount();
      // Custom order with some disabled
      const routine = account.routine;
      routine.chores = [
        { id: 'collect-gem-mine', enabled: true, order: 0 },
        { id: 'clan-check-in', enabled: false, order: 1 }, // disabled
        { id: 'claim-playtime-rewards', enabled: true, order: 2 },
        { id: 'claim-quests', enabled: true, order: 3 },
        { id: 'collect-inbox', enabled: false, order: 4 }, // disabled
        { id: 'buy-free-market-items', enabled: true, order: 5 }
      ];

      await runRoutine(account, deps);

      // Only enabled chores, in order
      expect(fakePort.state.executedChores).toEqual([
        'collect-gem-mine',
        'claim-playtime-rewards',
        'claim-quests',
        'buy-free-market-items'
      ]);
    });

    it('normalizes order when orders have gaps', async () => {
      const account = createTestAccount();
      // Orders with gaps
      const routine = account.routine;
      routine.chores = [
        { id: 'claim-playtime-rewards', enabled: true, order: 10 },
        { id: 'claim-quests', enabled: true, order: 5 },
        { id: 'collect-inbox', enabled: true, order: 20 },
        { id: 'buy-free-market-items', enabled: true, order: 1 },
        { id: 'collect-gem-mine', enabled: true, order: 15 },
        { id: 'clan-check-in', enabled: true, order: 30 }
      ];

      await runRoutine(account, deps);

      // Should execute in sorted order by order value
      expect(fakePort.state.executedChores).toEqual([
        'buy-free-market-items',      // order 1
        'claim-quests',               // order 5
        'claim-playtime-rewards',     // order 10
        'collect-gem-mine',           // order 15
        'collect-inbox',              // order 20
        'clan-check-in'               // order 30
      ]);
    });
  });

  describe('account switching', () => {
    it('switches to the correct account before running', async () => {
      const account = createTestAccount();

      await runRoutine(account, deps);

      expect(fakePort.state.switchedAccounts).toEqual(['test-account']);
    });

    it('switches account only once per run', async () => {
      const account = createTestAccount();

      await runRoutine(account, deps);

      expect(fakePort.state.switchedAccounts).toHaveLength(1);
    });
  });

  describe('run results', () => {
    it('returns results for each executed chore', async () => {
      const account = createTestAccount();
      account.routine.chores[0].enabled = false; // Disable first

      const run = await runRoutine(account, deps);

      expect(run.accountId).toBe('test-account');
      expect(run.results).toHaveLength(5);
      expect(run.startedAt).toBeInstanceOf(Date);
      expect(run.finishedAt).toBeInstanceOf(Date);
      expect(run.finishedAt!.getTime()).toBeGreaterThanOrEqual(run.startedAt.getTime());

      // Each result should have correct structure
      for (const result of run.results) {
        expect(result.choreId).toBeDefined();
        expect(typeof result.success).toBe('boolean');
        expect(result.timestamp).toBeInstanceOf(Date);
      }
    });

    it('includes chore results in order of execution', async () => {
      const account = createTestAccount();
      // Custom order
      account.routine.chores = [
        { id: 'collect-gem-mine', enabled: true, order: 0 },
        { id: 'clan-check-in', enabled: true, order: 1 }
      ];

      const run = await runRoutine(account, deps);

      expect(run.results.map(r => r.choreId)).toEqual([
        'collect-gem-mine',
        'clan-check-in'
      ]);
    });
  });

  describe('custom chore results from fake port', () => {
    it('uses custom results set on fake port', async () => {
      const account = createTestAccount();
      fakePort.setChoreResult('claim-playtime-rewards', {
        choreId: 'claim-playtime-rewards',
        success: false,
        message: 'Custom error',
        timestamp: new Date()
      });

      const run = await runRoutine(account, deps);

      const result = run.results.find(r => r.choreId === 'claim-playtime-rewards');
      expect(result).toBeDefined();
      expect(result!.success).toBe(false);
      expect(result!.message).toBe('Custom error');
    });
  });
});

describe('Default Routine', () => {
  it('contains all six v1 chores', () => {
    const routine = createDefaultRoutine();
    expect(routine.chores).toHaveLength(6);
  });

  it('has all chores enabled by default', () => {
    const routine = createDefaultRoutine();
    expect(routine.chores.every(c => c.enabled)).toBe(true);
  });

  it('has chores in correct default order', () => {
    const routine = createDefaultRoutine();
    const ids = routine.chores.map(c => c.id);
    expect(ids).toEqual([
      'claim-playtime-rewards',
      'claim-quests',
      'collect-inbox',
      'buy-free-market-items',
      'collect-gem-mine',
      'clan-check-in'
    ]);
  });

  it('has sequential orders starting from 0', () => {
    const routine = createDefaultRoutine();
    const orders = routine.chores.map(c => c.order);
    expect(orders).toEqual([0, 1, 2, 3, 4, 5]);
  });
});

describe('Account creation', () => {
  it('creates account with default routine', () => {
    const account = createAccount('acc-1', 'My Account');
    expect(account.id).toBe('acc-1');
    expect(account.name).toBe('My Account');
    expect(account.routine.chores).toHaveLength(6);
    expect(account.routine.chores.every(c => c.enabled)).toBe(true);
  });
});