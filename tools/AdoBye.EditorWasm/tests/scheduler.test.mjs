import test from 'node:test';
import assert from 'node:assert/strict';
import { Scheduler, Priority, CancelledError, PoolUnavailableError, replicaBudget, planBands } from '../wwwroot/scheduler.js';

// Deterministic clock: timers fire only when advance() is called.
class VirtualClock {
  t = 0; #timers = []; #id = 0;
  now() { return this.t; }
  setTimeout(fn, ms) { const id = ++this.#id; this.#timers.push({ id, at: this.t + ms, fn }); return id; }
  clearTimeout(id) { this.#timers = this.#timers.filter(x => x.id !== id); }
  advance(ms) {
    this.t += ms;
    for (const timer of this.#timers.filter(x => x.at <= this.t).sort((a, b) => a.at - b.at)) { this.clearTimeout(timer.id); timer.fn(); }
  }
}

const tick = () => new Promise(resolve => setImmediate(resolve));
const flush = async () => { for (let i = 0; i < 5; i++) await tick(); };
const gate = () => { let open; const promise = new Promise(r => { open = r; }); return { promise, open }; };

function pool(options = {}) {
  const clock = new VirtualClock(), engines = [];
  const scheduler = new Scheduler({
    maxSlots: 2, idleMs: 1000, clock,
    spawn: async () => { const e = { id: engines.length, terminated: false, terminate() { this.terminated = true; } }; engines.push(e); return e; },
    ...options,
  });
  return { scheduler, clock, engines };
}

test('runs higher priorities first, FIFO within a priority', async () => {
  const { scheduler } = pool({ maxSlots: 1 });
  const order = [], first = gate();
  const blocker = scheduler.submit({ priority: Priority.Visible, run: () => first.promise });
  await flush();
  const jobs = [
    scheduler.submit({ priority: Priority.Background, run: () => order.push('bg') }),
    scheduler.submit({ priority: Priority.Prefetch, run: () => order.push('prefetch-1') }),
    scheduler.submit({ priority: Priority.Input, run: () => order.push('input') }),
    scheduler.submit({ priority: Priority.Prefetch, run: () => order.push('prefetch-2') }),
  ];
  first.open(); await blocker; await Promise.all(jobs);
  assert.deepEqual(order, ['input', 'prefetch-1', 'prefetch-2', 'bg']);
});

test('spawns lazily, never beyond maxSlots, and only when work outstrips idle capacity', async () => {
  const { scheduler, engines } = pool({ maxSlots: 3 });
  assert.equal(engines.length, 0);
  const g = gate();
  const jobs = [1, 2, 3, 4, 5].map(() => scheduler.submit({ run: () => g.promise }));
  await flush();
  assert.equal(engines.length, 3);
  g.open(); await Promise.all(jobs);
  const single = pool({ maxSlots: 3 });
  await single.scheduler.submit({ run: async () => 1 });
  assert.equal(single.engines.length, 1);
});

test('spawn threshold keeps light work off extra replicas', async () => {
  const { scheduler, engines } = pool({ maxSlots: 4, spawnThreshold: 10 });
  const g = gate();
  const jobs = [1, 2, 3].map(() => scheduler.submit({ weight: 1, run: () => g.promise }));
  await flush();
  assert.equal(engines.length, 0, 'queued weight 3 < threshold 10');
  const heavy = scheduler.submit({ weight: 20, run: () => g.promise });
  await flush();
  assert.ok(engines.length >= 1);
  g.open(); await Promise.all([...jobs, heavy]);
});

test('terminates idle replicas on the virtual clock', async () => {
  const { scheduler, clock, engines } = pool();
  await scheduler.submit({ run: async () => 1 });
  await flush();
  clock.advance(999); assert.equal(engines[0].terminated, false);
  clock.advance(2); assert.equal(engines[0].terminated, true);
  assert.equal(scheduler.slotCount, 0);
  await scheduler.submit({ run: async () => 2 });
  assert.equal(engines.length, 2, 'a new replica is spawned on demand');
});

test('a busy replica is not retired by its idle timer', async () => {
  const { scheduler, clock, engines } = pool();
  const g = gate(), job = scheduler.submit({ run: () => g.promise });
  await flush(); clock.advance(5000);
  assert.equal(engines[0].terminated, false);
  g.open(); await job;
});

test('cancel rejects queued tasks, aborts running ones and leaves others', async () => {
  const { scheduler } = pool({ maxSlots: 1 });
  const g = gate(); let aborted = false;
  const running = scheduler.submit({ group: { page: 1 }, run: (_, signal) => { signal.addEventListener('abort', () => { aborted = true; }); return g.promise; } });
  await flush();
  const queuedOld = scheduler.submit({ group: { page: 1 }, run: async () => 'old' });
  const queuedNew = scheduler.submit({ group: { page: 2 }, run: async () => 'new' });
  const outcomes = [running, queuedOld].map(p => p.then(() => 'ok', e => e));
  assert.equal(scheduler.cancel(t => t.group.page === 1), 2);
  g.open();
  const results = await Promise.all(outcomes);
  assert.ok(results.every(e => e instanceof CancelledError && e.name === 'AbortError'));
  assert.equal(aborted, true);
  assert.equal(await queuedNew, 'new');
  assert.equal(scheduler.stats.cancelled, 2);
});

test('fatal slot error retries once on a fresh replica', async () => {
  const { scheduler, engines } = pool({ maxSlots: 1 });
  let calls = 0;
  const result = await scheduler.submit({
    run: async engine => { calls++; if (calls === 1) throw Object.assign(new Error('crash'), { fatal: true }); return engine.id; },
  });
  assert.equal(calls, 2);
  assert.equal(engines[0].terminated, true);
  assert.equal(result, 1);
  assert.equal(scheduler.disabled, false);
});

test('repeated failures disable the pool so callers fall back', async () => {
  const { scheduler } = pool({ maxSlots: 1, maxFailures: 2 });
  const failing = () => scheduler.submit({ run: async () => { throw Object.assign(new Error('crash'), { fatal: true }); } });
  await assert.rejects(failing(), /crash/);
  assert.equal(scheduler.disabled, true);
  await assert.rejects(scheduler.submit({ run: async () => 1 }), PoolUnavailableError);
});

test('boot failure falls back instead of hanging', async () => {
  const clock = new VirtualClock();
  const scheduler = new Scheduler({ maxSlots: 2, clock, maxFailures: 1, spawn: async () => { throw new Error('boot failed'); } });
  await assert.rejects(scheduler.submit({ run: async () => 1 }), PoolUnavailableError);
  assert.equal(scheduler.disabled, true);
});

test('memory budget stays single-worker on low-core or low-memory devices', () => {
  assert.equal(replicaBudget({ hardwareConcurrency: 1 }), 0);
  assert.equal(replicaBudget({ hardwareConcurrency: 2 }), 0);
  assert.equal(replicaBudget({ hardwareConcurrency: 8, deviceMemory: 2 }), 0);
  assert.equal(replicaBudget({ hardwareConcurrency: 8, saveData: true }), 0);
  assert.equal(replicaBudget({ hardwareConcurrency: 4, deviceMemory: 8 }), 3);
  assert.equal(replicaBudget({ hardwareConcurrency: 16, deviceMemory: 8 }), 4);
  assert.equal(replicaBudget({ hardwareConcurrency: 16, deviceMemory: 4 }), 4);
  assert.equal(replicaBudget({ hardwareConcurrency: 16, budgetMb: 200, perReplicaMb: 96 }), 2, 'total replica memory is capped');
});

test('band planning covers every row exactly once', () => {
  for (const [height, parts] of [[2800, 8], [100, 8], [1, 4], [2801, 3], [4096, 6]]) {
    const bands = planBands(height, parts, 128);
    assert.equal(bands[0][0], 0); assert.equal(bands.at(-1)[1], height);
    for (let i = 1; i < bands.length; i++) assert.equal(bands[i][0], bands[i - 1][1]);
    assert.ok(bands.length <= parts);
  }
});
