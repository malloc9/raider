/**
 * Orchestrates Runs: executes an Account's Routine using a GamePort.
 */

import type { Account, Routine, ChoreResult, Run, GamePort } from '../shared/types';

export interface OrchestratorDeps {
  gamePort: GamePort;
}

/**
 * Run an Account's Routine.
 * - Skips disabled chores
 * - Executes enabled chores in order
 * - Returns a Run with all results
 */
export async function runRoutine(account: Account, deps: OrchestratorDeps): Promise<Run> {
  const { gamePort } = deps;
  const startedAt = new Date();
  const results: ChoreResult[] = [];

  // Switch to the account
  await gamePort.switchAccount(account.id);

  // Get enabled chores sorted by order
  const enabledChores = account.routine.chores
    .filter(c => c.enabled)
    .sort((a, b) => a.order - b.order);

  // Execute each enabled chore in order
  for (const routineChore of enabledChores) {
    const result = await gamePort.executeChore(routineChore.id);
    results.push(result);
  }

  const finishedAt = new Date();

  return {
    accountId: account.id,
    startedAt,
    finishedAt,
    results
  };
}

/**
 * Run routines for multiple accounts sequentially.
 */
export async function runRoutines(accounts: Account[], deps: OrchestratorDeps): Promise<Run[]> {
  const runs: Run[] = [];
  for (const account of accounts) {
    const run = await runRoutine(account, deps);
    runs.push(run);
  }
  return runs;
}