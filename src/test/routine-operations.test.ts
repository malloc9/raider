/**
 * Tests for routine operations: reorder and toggle.
 */

import { settingsPort, resetSettings } from '../main/settings';
import { createAccount, type Routine, type ChoreId, V1_CHORES } from '../shared/types';

describe('Routine Operations', () => {
  beforeEach(() => {
    resetSettings();
  });

  afterAll(() => {
    resetSettings();
  });

  async function setupAccount(routine?: Routine) {
    const account = createAccount('acc-1', 'Test');
    if (routine) account.routine = routine;
    await settingsPort.saveAccounts([account]);
    return account;
  }

  describe('toggle chore', () => {
    it('disables a chore', async () => {
      await setupAccount();
      const routine = await settingsPort.loadRoutine('acc-1');
      expect(routine!.chores[0].enabled).toBe(true);

      // Simulate toggle via the same logic as IPC handler
      const choreId: ChoreId = 'claim-playtime-rewards';
      const updatedRoutine = { ...routine!, chores: routine!.chores.map(c =>
        c.id === choreId ? { ...c, enabled: false } : c
      )};
      await settingsPort.saveRoutine('acc-1', updatedRoutine);

      const loaded = await settingsPort.loadRoutine('acc-1');
      const chore = loaded!.chores.find(c => c.id === choreId);
      expect(chore!.enabled).toBe(false);
    });

    it('enables a disabled chore', async () => {
      const routine: Routine = {
        chores: V1_CHORES.map((c, i) => ({ id: c.id, enabled: false, order: i }))
      };
      await setupAccount(routine);

      const loaded = await settingsPort.loadRoutine('acc-1');
      expect(loaded!.chores[0].enabled).toBe(false);

      // Toggle on
      const choreId: ChoreId = 'claim-playtime-rewards';
      const updatedRoutine = { ...loaded!, chores: loaded!.chores.map(c =>
        c.id === choreId ? { ...c, enabled: true } : c
      )};
      await settingsPort.saveRoutine('acc-1', updatedRoutine);

      const reloaded = await settingsPort.loadRoutine('acc-1');
      const chore = reloaded!.chores.find(c => c.id === choreId);
      expect(chore!.enabled).toBe(true);
    });

    it('toggling one chore does not affect others', async () => {
      await setupAccount();

      const choreId: ChoreId = 'claim-quests';
      const routine = await settingsPort.loadRoutine('acc-1');
      const updatedRoutine = { ...routine!, chores: routine!.chores.map(c =>
        c.id === choreId ? { ...c, enabled: false } : c
      )};
      await settingsPort.saveRoutine('acc-1', updatedRoutine);

      const loaded = await settingsPort.loadRoutine('acc-1');
      expect(loaded!.chores.find(c => c.id === 'claim-quests')!.enabled).toBe(false);
      expect(loaded!.chores.find(c => c.id === 'claim-playtime-rewards')!.enabled).toBe(true);
      expect(loaded!.chores.find(c => c.id === 'collect-inbox')!.enabled).toBe(true);
    });
  });

  describe('reorder chores', () => {
    it('moves a chore to a new position', async () => {
      await setupAccount();

      // Move 'collect-gem-mine' (index 4) to position 0
      const routine = await settingsPort.loadRoutine('acc-1');
      const choreId: ChoreId = 'collect-gem-mine';
      const newOrder = 0;

      // Reorder logic (same as IPC handler)
      const chore = routine!.chores.find(c => c.id === choreId)!;
      const otherChores = routine!.chores.filter(c => c.id !== choreId);
      for (const c of otherChores) {
        if (c.order >= newOrder) c.order++;
      }
      chore.order = newOrder;
      routine!.chores = [...otherChores, chore].sort((a, b) => a.order - b.order);
      routine!.chores.forEach((c, i) => { c.order = i; });

      await settingsPort.saveRoutine('acc-1', routine!);

      const loaded = await settingsPort.loadRoutine('acc-1');
      expect(loaded!.chores[0].id).toBe('collect-gem-mine');
      expect(loaded!.chores[1].id).toBe('claim-playtime-rewards');
      expect(loaded!.chores[5].id).toBe('clan-check-in');
    });

    it('moves a chore to the end', async () => {
      await setupAccount();

      const routine = await settingsPort.loadRoutine('acc-1');
      const choreId: ChoreId = 'claim-playtime-rewards';
      const newOrder = 5; // Last position

      const chore = routine!.chores.find(c => c.id === choreId)!;
      const otherChores = routine!.chores.filter(c => c.id !== choreId);
      for (const c of otherChores) {
        if (c.order >= newOrder) c.order++;
      }
      chore.order = newOrder;
      routine!.chores = [...otherChores, chore].sort((a, b) => a.order - b.order);
      routine!.chores.forEach((c, i) => { c.order = i; });

      await settingsPort.saveRoutine('acc-1', routine!);

      const loaded = await settingsPort.loadRoutine('acc-1');
      expect(loaded!.chores[5].id).toBe('claim-playtime-rewards');
    });

    it('moves a chore to the middle', async () => {
      await setupAccount();

      const routine = await settingsPort.loadRoutine('acc-1');
      // Move 'clan-check-in' (last) to position 2
      const choreId: ChoreId = 'clan-check-in';
      const newOrder = 2;

      const chore = routine!.chores.find(c => c.id === choreId)!;
      const otherChores = routine!.chores.filter(c => c.id !== choreId);
      for (const c of otherChores) {
        if (c.order >= newOrder) c.order++;
      }
      chore.order = newOrder;
      routine!.chores = [...otherChores, chore].sort((a, b) => a.order - b.order);
      routine!.chores.forEach((c, i) => { c.order = i; });

      await settingsPort.saveRoutine('acc-1', routine!);

      const loaded = await settingsPort.loadRoutine('acc-1');
      expect(loaded!.chores[0].id).toBe('claim-playtime-rewards');
      expect(loaded!.chores[1].id).toBe('claim-quests');
      expect(loaded!.chores[2].id).toBe('clan-check-in');
      expect(loaded!.chores[3].id).toBe('collect-inbox');
    });

    it('preserves enabled state during reorder', async () => {
      const routine: Routine = {
        chores: [
          { id: 'claim-playtime-rewards', enabled: true, order: 0 },
          { id: 'claim-quests', enabled: false, order: 1 },
          { id: 'collect-inbox', enabled: true, order: 2 },
          { id: 'buy-free-market-items', enabled: true, order: 3 },
          { id: 'collect-gem-mine', enabled: false, order: 4 },
          { id: 'clan-check-in', enabled: true, order: 5 }
        ]
      };
      await setupAccount(routine);

      // Move disabled 'claim-quests' to position 0
      const loaded = await settingsPort.loadRoutine('acc-1');
      const choreId: ChoreId = 'claim-quests';
      const newOrder = 0;

      const chore = loaded!.chores.find(c => c.id === choreId)!;
      const otherChores = loaded!.chores.filter(c => c.id !== choreId);
      for (const c of otherChores) {
        if (c.order >= newOrder) c.order++;
      }
      chore.order = newOrder;
      loaded!.chores = [...otherChores, chore].sort((a, b) => a.order - b.order);
      loaded!.chores.forEach((c, i) => { c.order = i; });

      await settingsPort.saveRoutine('acc-1', loaded!);

      const reloaded = await settingsPort.loadRoutine('acc-1');
      // 'claim-quests' should still be disabled
      expect(reloaded!.chores[0].id).toBe('claim-quests');
      expect(reloaded!.chores[0].enabled).toBe(false);
      // Others should preserve their enabled state
      expect(reloaded!.chores[1].id).toBe('claim-playtime-rewards');
      expect(reloaded!.chores[1].enabled).toBe(true);
    });

    it('normalizes orders to be sequential after reorder', async () => {
      await setupAccount();

      const routine = await settingsPort.loadRoutine('acc-1');
      // Move to position 2
      const choreId: ChoreId = 'clan-check-in';
      const newOrder = 2;

      const chore = routine!.chores.find(c => c.id === choreId)!;
      const otherChores = routine!.chores.filter(c => c.id !== choreId);
      for (const c of otherChores) {
        if (c.order >= newOrder) c.order++;
      }
      chore.order = newOrder;
      routine!.chores = [...otherChores, chore].sort((a, b) => a.order - b.order);
      routine!.chores.forEach((c, i) => { c.order = i; });

      await settingsPort.saveRoutine('acc-1', routine!);

      const loaded = await settingsPort.loadRoutine('acc-1');
      const orders = loaded!.chores.map(c => c.order);
      expect(orders).toEqual([0, 1, 2, 3, 4, 5]);
    });
  });

  describe('combined operations', () => {
    it('toggle and reorder can be combined', async () => {
      await setupAccount();

      // 1. Disable 'claim-quests'
      let routine = await settingsPort.loadRoutine('acc-1');
      routine = { ...routine!, chores: routine!.chores.map(c =>
        c.id === 'claim-quests' ? { ...c, enabled: false } : c
      )};
      await settingsPort.saveRoutine('acc-1', routine);

      // 2. Move 'collect-gem-mine' to front
      routine = await settingsPort.loadRoutine('acc-1');
      const chore = routine!.chores.find(c => c.id === 'collect-gem-mine')!;
      const otherChores = routine!.chores.filter(c => c.id !== 'collect-gem-mine');
      for (const c of otherChores) if (c.order >= 0) c.order++;
      chore.order = 0;
      routine!.chores = [...otherChores, chore].sort((a, b) => a.order - b.order);
      routine!.chores.forEach((c, i) => { c.order = i; });
      await settingsPort.saveRoutine('acc-1', routine);

      // Verify both changes
      const final = await settingsPort.loadRoutine('acc-1');
      expect(final!.chores[0].id).toBe('collect-gem-mine');
      expect(final!.chores[0].enabled).toBe(true);
      expect(final!.chores[1].id).toBe('claim-playtime-rewards');
      expect(final!.chores[2].id).toBe('claim-quests');
      expect(final!.chores[2].enabled).toBe(false);
    });
  });
});