// Render-replica pool: parallel, read-only page rendering for the browser editor (no SharedArrayBuffer / WASM threads needed).
//
// The primary PDF worker still owns every mutation. Replicas open the primary's saved snapshot (GET /api/snapshot, keyed by the document
// version in the page-image URL) and render disjoint horizontal bands of a page, or whole thumbnails. Band rows are bit-identical to a
// whole-page render (see BandRenderTests), so stitching them changes no pixels. Any problem (boot failure, crash, stale snapshot,
// missing OffscreenCanvas, low-core/low-memory device) makes tryRender() resolve null and the caller uses the primary worker as before.
import { Scheduler, Priority, PoolUnavailableError, planBands, replicaBudget } from './scheduler.js';

const pagePattern = /^\/api\/page\/(\d+)\.png$/;
const fatal = message => Object.assign(new Error(message), { fatal: true });

export function parsePageImageUrl(url, base = globalThis.location?.href ?? 'http://localhost/') {
  const parsed = new URL(url, base);
  const match = pagePattern.exec(parsed.pathname);
  if (!match || !parsed.searchParams.has('v')) return null;
  return {
    page: Number(match[1]), dpi: Number(parsed.searchParams.get('dpi') ?? 48),
    version: Number(parsed.searchParams.get('v')), bare: parsed.searchParams.get('bare') === 'true',
  };
}

class Replica {
  #worker; #next = 0; #pending = new Map(); loadedKey = null;
  constructor(worker) {
    this.#worker = worker;
    worker.onmessage = ({ data }) => {
      const entry = this.#pending.get(data.id);
      if (!entry) return;
      this.#pending.delete(data.id);
      data.error ? entry.reject(new Error(data.error)) : entry.resolve(data);
    };
    const die = message => { for (const { reject } of this.#pending.values()) reject(fatal(message)); this.#pending.clear(); };
    worker.onerror = event => die(event.message || 'A render replica stopped.');
    worker.onmessageerror = () => die('A render replica returned an unreadable response.');
  }
  call(message, transfer = []) {
    return new Promise((resolve, reject) => {
      const id = ++this.#next;
      this.#pending.set(id, { resolve, reject });
      try { this.#worker.postMessage({ ...message, id }, transfer); }
      catch (error) { this.#pending.delete(id); reject(fatal(error.message)); }
    });
  }
  async ensure(key, loadBytes) {
    if (this.loadedKey === key) return;
    this.loadedKey = null;
    await this.call({ op: 'load', key, pdf: await loadBytes() }, []);
    this.loadedKey = key;
  }
  terminate() {
    for (const { reject } of this.#pending.values()) reject(fatal('The render replica was terminated.'));
    this.#pending.clear(); this.#worker.terminate();
  }
}

function spawnReplica(workerUrl, bootTimeoutMs = 60000) {
  return new Promise((resolve, reject) => {
    const worker = new Worker(workerUrl, { type: 'module', name: 'PDF render replica' });
    const timer = setTimeout(() => { worker.terminate(); reject(fatal('A render replica did not boot in time.')); }, bootTimeoutMs);
    worker.onmessage = ({ data }) => {
      clearTimeout(timer);
      if (data.startupError) { worker.terminate(); reject(fatal(data.startupError)); return; }
      if (data.ready) resolve(new Replica(worker));
    };
    worker.onerror = event => { clearTimeout(timer); worker.terminate(); reject(fatal(event.message || 'A render replica failed to start.')); };
  });
}

/**
 * @param snapshot  async (bare) => ({ bytes: Uint8Array, version: number }) from the primary engine (GET /api/snapshot).
 * @param spawn     optional async () => replica engine; defaults to a real Worker (tests inject fakes).
 */
export function createRenderPool({
  snapshot, workerUrl = new URL('replica-worker.js', import.meta.url), spawn, env = globalThis, clock, maxSlots,
  idleMs = 30000, tileMinPixels = 1_500_000, maxTilePixels = 64_000_000, burstSize = 4, burstWindowMs = 200,
}) {
  const budget = maxSlots ?? replicaBudget({
    hardwareConcurrency: env.navigator?.hardwareConcurrency, deviceMemory: env.navigator?.deviceMemory,
    saveData: env.navigator?.connection?.saveData,
  });
  const canStitch = typeof env.OffscreenCanvas === 'function';
  const scheduler = new Scheduler({
    maxSlots: budget, idleMs, clock, spawnThreshold: 1,
    spawn: spawn ?? (() => spawnReplica(workerUrl)),
  });
  const snapshots = new Map();
  const recent = [];
  const timings = [];
  const now = () => (clock ?? performance).now();

  const getSnapshot = (version, bare) => {
    const key = `${version}:${bare}`;
    if (!snapshots.has(key)) {
      const promise = snapshot(bare).then(result => {
        if (result.version !== version) return null;
        return result.bytes;
      });
      promise.catch(() => snapshots.delete(key));
      snapshots.set(key, promise);
      while (snapshots.size > 3) snapshots.delete(snapshots.keys().next().value);
    }
    return snapshots.get(key).then(bytes => { if (!bytes) snapshots.delete(key); return bytes; });
  };

  const supersede = (request, priority) => scheduler.cancel(task => {
    const g = task.group;
    return g && (g.version < request.version ||
      (priority <= Priority.Visible && task.priority === Priority.Visible && g.page !== request.page && g.version === request.version) ||
      (priority <= Priority.Visible && task.priority >= Priority.Prefetch && g.version < request.version));
  });

  const isBurst = () => {
    const t = now();
    recent.push(t);
    while (recent.length && t - recent[0] > burstWindowMs) recent.shift();
    return recent.length >= burstSize;
  };

  async function tryRender(url, { priority } = {}) {
    if (scheduler.disabled || budget <= 0) return null;
    const request = parsePageImageUrl(url);
    if (!request) return null;
    const { page, dpi, version, bare } = request;
    const estimated = (dpi / 72) ** 2 * 612 * 792;
    const tiled = estimated >= tileMinPixels;
    priority ??= dpi >= 100 ? Priority.Visible : Priority.Prefetch;
    // Heavy rasters always benefit; small ones only once a burst (thumbnail sidebar, prefetch) is arriving or replicas are already warm.
    if (!tiled && !isBurst() && scheduler.slotCount === 0) return null;
    if (tiled && !canStitch) return null;
    const key = `${version}:${bare}`;
    const group = { page, version };
    const loadBytes = async () => {
      const bytes = await getSnapshot(version, bare);
      if (!bytes) throw Object.assign(new Error('stale snapshot'), { stale: true });
      return bytes.slice().buffer;
    };
    supersede(request, priority);
    try {
      if (!tiled) {
        const reply = await scheduler.submit({
          priority, group, weight: 1,
          run: async replica => { await replica.ensure(key, loadBytes); return replica.call({ op: 'png', page, dpi }); },
        });
        return new Blob([reply.png], { type: 'image/png' });
      }
      const t0 = now();
      const size = await scheduler.submit({
        priority, group, weight: 1,
        run: async replica => { await replica.ensure(key, loadBytes); return replica.call({ op: 'size', page, dpi }); },
      });
      if (size.width * size.height > maxTilePixels) return null;
      const t1 = now();
      const bands = planBands(size.height, Math.max(budget, 1) * (env.poolBandFactor ?? 1), 192);
      const canvas = new env.OffscreenCanvas(size.width, size.height);
      const context = canvas.getContext('2d');
      const workerMs = [];
      await Promise.all(bands.map(([y0, y1]) => scheduler.submit({
        priority, group, weight: (y1 - y0) * size.width,
        run: async replica => { await replica.ensure(key, loadBytes); return replica.call({ op: 'band', page, dpi, y0, y1 }); },
      }).then(reply => {
        workerMs.push(Math.round(reply.ms));
        context.putImageData(new ImageData(new Uint8ClampedArray(reply.rgba), size.width, y1 - y0), 0, y0);
      })));
      const t2 = now();
      const blob = await canvas.convertToBlob({ type: 'image/png' });
      timings.push({ page, dpi, bands: bands.length, workerMs, planMs: t1 - t0, bandsMs: t2 - t1, encodeMs: now() - t2 });
      if (timings.length > 32) timings.shift();
      return blob;
    } catch (error) {
      if (error?.stale || error instanceof PoolUnavailableError) return null;
      throw error;
    }
  }

  return { tryRender, scheduler, timings, get enabled() { return !scheduler.disabled && budget > 0; }, maxSlots: budget, dispose: () => scheduler.dispose() };
}
