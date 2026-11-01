import test from 'node:test';
import assert from 'node:assert/strict';
import { buildOutlineIndex, currentOutlineItem, ancestorsOf, visibleRows, initiallyExpanded } from './wwwroot/outline-map.mjs';

const title = (index, page, pinned) => index.nodes[currentOutlineItem(index, page, pinned)]?.title ?? null;

test('nested items pick the latest started section', () => {
  const index = buildOutlineIndex([
    { title: 'A', page: 0, children: [{ title: 'A1', page: 1 }, { title: 'A2', page: 3 }] },
    { title: 'B', page: 5, children: [{ title: 'B1', page: 6 }] },
  ]);
  assert.deepEqual([0, 1, 2, 3, 4, 5, 6, 7].map(p => title(index, p)), ['A', 'A1', 'A1', 'A2', 'A2', 'B', 'B1', 'B1']);
  assert.deepEqual(ancestorsOf(index, currentOutlineItem(index, 6)).map(i => index.nodes[i].title), ['B']);
});

test('before the first destination nothing is current', () => {
  assert.equal(title(buildOutlineIndex([{ title: 'Late', page: 4 }]), 2), null);
});

test('unordered page destinations use the greatest page not after the active one', () => {
  const index = buildOutlineIndex([{ title: 'Z', page: 8 }, { title: 'A', page: 2 }, { title: 'M', page: 5 }]);
  assert.deepEqual([1, 2, 4, 5, 7, 8, 9].map(p => title(index, p)), [null, 'A', 'A', 'M', 'M', 'Z', 'Z']);
});

test('items without a page destination are ignored', () => {
  const index = buildOutlineIndex([
    { title: 'Link', page: null }, { title: 'Chapter', page: 1, children: [{ title: 'Web', children: [] }, { title: 'Bad', page: -3 }, { title: 'Sec', page: 2 }] }, {},
  ]);
  assert.deepEqual([0, 1, 2, 3].map(p => title(index, p)), [null, 'Chapter', 'Sec', 'Sec']);
  assert.equal(index.sorted.length, 2);
});

test('same-page items resolve to the last one in document order, duplicates included', () => {
  const index = buildOutlineIndex([
    { title: 'Ch', page: 2, children: [{ title: 'Ch.1', page: 2 }, { title: 'Ch.2', page: 2 }] },
    { title: 'Dup', page: 2 },
  ]);
  assert.equal(title(index, 2), 'Dup');
  assert.equal(title(index, 3), 'Dup');
  assert.equal(title(buildOutlineIndex([{ title: 'Ch', page: 2, children: [{ title: 'Ch.1', page: 2 }] }]), 2), 'Ch.1');
});

test('a pinned item survives only while the active page is its page', () => {
  const index = buildOutlineIndex([{ title: 'Ch', page: 2, children: [{ title: 'Ch.1', page: 2 }] }, { title: 'Next', page: 4 }]);
  assert.equal(title(index, 2, 0), 'Ch');
  assert.equal(title(index, 3, 0), 'Ch.1');
});

test('page operations are re-indexed from a fresh tree', () => {
  const before = buildOutlineIndex([{ title: 'A', page: 0 }, { title: 'B', page: 2 }, { title: 'C', page: 4 }]);
  assert.equal(title(before, 3), 'B');
  const afterMove = buildOutlineIndex([{ title: 'A', page: 0 }, { title: 'B', page: 4 }, { title: 'C', page: 2 }]);
  assert.equal(title(afterMove, 3), 'C');
  const afterDelete = buildOutlineIndex([{ title: 'A', page: 0 }, { title: 'B', children: [] }, { title: 'C', page: 3 }]);
  assert.equal(title(afterDelete, 2), 'A');
  const rotated = buildOutlineIndex([{ title: 'A', page: 0 }, { title: 'B', page: 2 }, { title: 'C', page: 4 }]);
  assert.equal(title(rotated, 3), 'B');
});

test('collapsed subtrees are skipped and open flags seed the expanded set', () => {
  const index = buildOutlineIndex([
    { title: 'A', page: 0, open: true, children: [{ title: 'A1', page: 1, children: [{ title: 'A1a', page: 1 }] }] },
    { title: 'B', page: 2, children: [{ title: 'B1', page: 3 }] },
  ]);
  const titles = rows => rows.map(i => index.nodes[i].title);
  assert.deepEqual(titles(visibleRows(index, initiallyExpanded(index))), ['A', 'A1', 'B']);
  assert.deepEqual(titles(visibleRows(index, new Set([0, 1, 3]))), ['A', 'A1', 'A1a', 'B', 'B1']);
  assert.deepEqual(titles(visibleRows(index, new Set())), ['A', 'B']);
  assert.deepEqual(index.nodes.map(n => [n.depth, n.pos, n.size]), [[0, 1, 2], [1, 1, 1], [2, 1, 1], [0, 2, 2], [1, 1, 1]]);
});

test('empty and malformed input is safe, and 10k items resolve quickly', () => {
  for (const bad of [undefined, null, [], 'x']) assert.equal(currentOutlineItem(buildOutlineIndex(bad), 3), -1);
  const items = Array.from({ length: 10000 }, (_, i) => ({ title: 'T' + i, page: i >> 1 }));
  const index = buildOutlineIndex(items);
  const start = performance.now();
  for (let p = 0; p < 5000; p++) assert.equal(index.nodes[currentOutlineItem(index, p)].title, 'T' + (p * 2 + 1));
  assert.ok(performance.now() - start < 1000);
  assert.equal(visibleRows(index, new Set()).length, 10000);
});
