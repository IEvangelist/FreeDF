// Pure page -> outline-item mapping. Items are { title, page?, open?, children? } in document order, as served by /api/outlines.

/** Flattens the tree depth-first and precomputes the page-sorted lookup used for binary search. */
export function buildOutlineIndex(items) {
  const nodes = [];
  const walk = (list, parent, depth) => {
    list.forEach((item, i) => {
      const id = nodes.length;
      const children = Array.isArray(item.children) ? item.children : [];
      const page = Number.isInteger(item.page) && item.page >= 0 ? item.page : -1;
      const node = { id, title: String(item.title ?? ''), page, parent, depth, open: item.open === true, first: -1, count: 0, size: list.length, pos: i + 1, end: id + 1 };
      nodes.push(node);
      if (children.length) {
        node.first = id + 1;
        walk(children, id, depth + 1);
        node.count = children.length;
        node.end = nodes.length;
      }
    });
  };
  walk(Array.isArray(items) ? items : [], -1, 0);
  const sorted = nodes.filter(n => n.page >= 0).sort((a, b) => a.page - b.page || a.id - b.id);
  return { nodes, sorted };
}

/**
 * The item whose page is the greatest one <= `page`. When several items start on that page the last one in
 * document order wins (the deepest section that has begun). `pinned` keeps an item the user just chose
 * while the active page still equals its page. Returns -1 when nothing applies.
 */
export function currentOutlineItem(index, page, pinned = -1) {
  if (pinned >= 0 && index.nodes[pinned]?.page === page) return pinned;
  const { sorted } = index;
  let lo = 0, hi = sorted.length;
  while (lo < hi) {
    const mid = (lo + hi) >>> 1;
    if (sorted[mid].page <= page) lo = mid + 1; else hi = mid;
  }
  return lo === 0 ? -1 : sorted[lo - 1].id;
}

export function ancestorsOf(index, id) {
  const out = [];
  for (let p = index.nodes[id]?.parent ?? -1; p >= 0; p = index.nodes[p].parent) out.push(p);
  return out;
}

/** Ids of the rows shown for an expanded set; collapsed subtrees are skipped without being visited. */
export function visibleRows(index, expanded) {
  const rows = [], { nodes } = index;
  for (let i = 0; i < nodes.length;) {
    rows.push(i);
    i = nodes[i].first >= 0 && !expanded.has(i) ? nodes[i].end : i + 1;
  }
  return rows;
}

export function initiallyExpanded(index) {
  return new Set(index.nodes.filter(n => n.first >= 0 && n.open).map(n => n.id));
}
