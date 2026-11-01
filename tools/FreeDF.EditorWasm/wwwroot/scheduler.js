// Priority scheduler for the render-replica pool. Pure logic (no DOM, workers or real timers) so it can be tested with a virtual clock.
//
// Concepts
//   slot  : one render replica (a PDF engine worker). Created lazily through `spawn()`, terminated when idle or on failure.
//   task  : { priority, group, run(slot, signal) } — `group` is an opaque tag used to cancel superseded work.
// Rules
//   * Lower priority number runs first; FIFO within a priority.
//   * A slot is only spawned when queued work exceeds the idle capacity AND the queued weight reaches `spawnThreshold`.
//   * Idle slots are terminated after `idleMs`; the pool never exceeds `maxSlots`.
//   * A fatal slot error removes the slot and retries the task once on another slot; after `maxFailures` fatal errors the pool disables
//     itself and every task (queued or new) rejects with PoolUnavailableError so callers can fall back to the primary engine.

export const Priority = Object.freeze({ Input: 0, Visible: 1, Prefetch: 2, Background: 3 });

export class PoolUnavailableError extends Error {
  constructor(message = 'The render pool is unavailable.') { super(message); this.name = 'PoolUnavailableError'; }
}

export class CancelledError extends Error {
  constructor(message = 'The render task was superseded.') { super(message); this.name = 'AbortError'; }
}

const realClock = { now: () => performance.now(), setTimeout: (f, ms) => setTimeout(f, ms), clearTimeout: id => clearTimeout(id) };

export class Scheduler {
  #slots = [];
  #queue = [];
  #seq = 0;
  #timers = new Map();
  #failures = 0;
  #pendingSpawns = 0;
  disabled = false;
  stats = { spawned: 0, terminated: 0, completed: 0, cancelled: 0, failures: 0 };

  constructor({ spawn, maxSlots, idleMs = 30000, spawnThreshold = 1, maxFailures = 3, clock = realClock }) {
    this.spawn = spawn; this.maxSlots = maxSlots; this.idleMs = idleMs;
    this.spawnThreshold = spawnThreshold; this.maxFailures = maxFailures; this.clock = clock;
  }

  get slotCount() { return this.#slots.length + this.#pendingSpawns; }
  get queued() { return this.#queue.length; }
  get busy() { return this.#slots.filter(s => s.busy).length; }

  submit({ priority = Priority.Visible, group = null, weight = 1, run }) {
    if (this.disabled || this.maxSlots <= 0) return Promise.reject(new PoolUnavailableError());
    return new Promise((resolve, reject) => {
      const controller = new AbortController();
      this.#queue.push({ priority, group, weight, run, resolve, reject, controller, seq: this.#seq++, attempts: 0 });
      this.#queue.sort((a, b) => a.priority - b.priority || a.seq - b.seq);
      this.#pump();
    });
  }

  // Cancels queued tasks and aborts running ones for which predicate(task) is true. Returns the number cancelled.
  cancel(predicate) {
    let count = 0;
    for (const task of [...this.#queue]) {
      if (!predicate(task)) continue;
      this.#queue.splice(this.#queue.indexOf(task), 1);
      task.reject(new CancelledError()); count++;
    }
    for (const slot of this.#slots) {
      const task = slot.task;
      if (task && !task.cancelled && predicate(task)) { task.cancelled = true; task.controller.abort(); count++; }
    }
    this.stats.cancelled += count;
    return count;
  }

  dispose() {
    this.disabled = true;
    for (const task of this.#queue.splice(0)) task.reject(new PoolUnavailableError());
    for (const slot of this.#slots.splice(0)) this.#retire(slot);
  }

  #pump() {
    if (this.disabled) return;
    for (const slot of this.#slots) {
      if (slot.busy) continue;
      const task = this.#queue.shift();
      if (!task) break;
      this.#start(slot, task);
    }
    // Spawn only when work outstrips idle capacity and is heavy enough to repay a runtime boot.
    const idle = this.#slots.filter(s => !s.busy).length + this.#pendingSpawns;
    const waiting = this.#queue.length;
    const weight = this.#queue.reduce((sum, t) => sum + t.weight, 0);
    if (waiting > idle && weight >= this.spawnThreshold && this.slotCount < this.maxSlots) this.#spawnSlot();
  }

  async #spawnSlot() {
    this.#pendingSpawns++;
    let engine;
    try { engine = await this.spawn(); }
    catch (error) {
      this.#pendingSpawns--;
      this.#fail(error);
      this.#pump();
      return;
    }
    this.#pendingSpawns--;
    if (this.disabled) { try { engine.terminate?.(); } catch {} return; }
    const slot = { engine, busy: false, task: null, idleTimer: 0 };
    this.#slots.push(slot); this.stats.spawned++;
    this.#pump();
    this.#arm(slot);
  }

  #start(slot, task) {
    slot.busy = true; slot.task = task;
    this.clock.clearTimeout(slot.idleTimer);
    Promise.resolve().then(() => task.run(slot.engine, task.controller.signal)).then(result => {
      this.stats.completed++;
      if (task.cancelled) task.reject(new CancelledError()); else task.resolve(result);
    }, error => {
      if (task.cancelled) { task.reject(new CancelledError()); return; }
      if (error?.fatal) {
        this.#slots.splice(this.#slots.indexOf(slot), 1);
        this.#retire(slot);
        this.#fail(error);
        if (++task.attempts < 2 && !this.disabled) { this.#queue.unshift(task); this.#queue.sort((a, b) => a.priority - b.priority || a.seq - b.seq); }
        else task.reject(error);
        this.#pump();
        return;
      }
      task.reject(error);
    }).finally(() => {
      slot.busy = false; slot.task = null;
      if (this.#slots.includes(slot)) { this.#pump(); this.#arm(slot); }
    });
  }

  #fail(error) {
    this.stats.failures++;
    if (++this.#failures < this.maxFailures) return;
    this.disabled = true;
    for (const task of this.#queue.splice(0)) task.reject(new PoolUnavailableError(error?.message));
    for (const slot of this.#slots.splice(0)) if (!slot.busy) this.#retire(slot);
  }

  #arm(slot) {
    if (slot.busy || this.disabled) return;
    this.clock.clearTimeout(slot.idleTimer);
    slot.idleTimer = this.clock.setTimeout(() => {
      if (slot.busy) return;
      const index = this.#slots.indexOf(slot);
      if (index >= 0) { this.#slots.splice(index, 1); this.#retire(slot); }
    }, this.idleMs);
  }

  #retire(slot) {
    this.clock.clearTimeout(slot.idleTimer);
    this.stats.terminated++;
    try { slot.engine.terminate?.(); } catch {}
  }
}

// Memory budget: every replica is a full .NET runtime (tens of MB, more while rendering a large raster).
// Returns the number of secondary replicas to allow; 0 means "stay single-worker".
export function replicaBudget({ hardwareConcurrency = 1, deviceMemory, saveData = false, budgetMb = 512, perReplicaMb = 96, max = 4 } = {}) {
  if (saveData || hardwareConcurrency <= 2 || (deviceMemory !== undefined && deviceMemory <= 2)) return 0;
  const byCores = Math.min(hardwareConcurrency - 1, max);
  const memoryMb = deviceMemory === undefined ? budgetMb : Math.min(budgetMb, deviceMemory * 1024 * 0.25);
  return Math.max(0, Math.min(byCores, Math.floor(memoryMb / perReplicaMb)));
}

// Splits `height` rows into up to `parts` contiguous bands of at least `minRows` rows (the last band absorbs the remainder).
export function planBands(height, parts, minRows = 128) {
  const count = Math.max(1, Math.min(parts, Math.floor(height / minRows)));
  const size = Math.ceil(height / count), bands = [];
  for (let y = 0; y < height; y += size) bands.push([y, Math.min(height, y + size)]);
  return bands;
}
