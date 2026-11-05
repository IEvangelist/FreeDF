const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const between = (start, end) => html.slice(html.indexOf(start), html.indexOf(end, html.indexOf(start)));
function harness(snap = true) {
  const context = vm.createContext({ status: message => { throw new Error(message); } });
  vm.runInContext(`let grid = { snap: ${snap}, spacing: 12 }, locked = false;
    function commit(fn) { fn(); }
    ${between('function snapPoint(', 'function paintGrid(')}
    ${between('function constrainBox(', 'function startMove(')}
    ${between('function imagePoint(', 'function paintImageGeometry(')}
    globalThis.api = { snapPoint, moveDelta, constrainBox, imagePoint, imageSourcePoint, imageCropBox, applyImageCrop, transformImage };`, context);
  return context.api;
}
const array = value => Array.from(value);
const close = (actual, expected) => actual.forEach((value, i) => assert.ok(Math.abs(value - expected[i]) < 1e-8, `${actual} != ${expected}`));

test('grid snapping is opt-in, works in page points, and supports a temporary bypass', () => {
  close(array(harness(false).snapPoint(17, 26)), [17, 26]);
  const h = harness();
  close(array(h.snapPoint(17, 26)), [12, 24]);
  close(array(h.snapPoint(-17, -26)), [-12, -24]);
  for (const event of [{ ctrlKey: true }, { metaKey: true }]) close(array(h.snapPoint(17, 26, event)), [17, 26]);
});

test('Shift movement keeps its fixed axis even when its original position is off-grid', () => {
  const h = harness();
  close(array(h.moveDelta(17, 19, 29, 2, { shiftKey: true })), [31, 0]);
  close(array(h.moveDelta(17, 19, 2, 29, { shiftKey: true })), [0, 29]);
  close(array(h.moveDelta(17, 19, 29, 2, { shiftKey: true, ctrlKey: true })), [29, 0]);
});

test('snapped resizing preserves image proportions and Alt center resizing', () => {
  const h = harness();
  const box = array(h.constrainBox([10, 20, 110, 70], 'se', 23, 8, {}, true));
  assert.equal((box[2] - box[0]) / (box[3] - box[1]), 2);
  assert.ok(box[2] % 12 === 0 || box[3] % 12 === 0, 'the dominant resize edge stays on the grid');
  const centered = array(h.constrainBox([10, 20, 110, 70], 'se', 23, 8, { altKey: true }, true));
  close([(centered[0] + centered[2]) / 2, (centered[1] + centered[3]) / 2], [60, 45]);
  assert.equal((centered[2] - centered[0]) / (centered[3] - centered[1]), 2);
});

test('crop and full-image restoration keep every retained pixel in place under all transforms', () => {
  const h = harness();
  for (const rotation of [0, 90, 180, 270]) for (const flipX of [false, true]) for (const flipY of [false, true]) {
    const image = { x: 30, y: 40, w: 200, h: 120, rotation, flipX, flipY };
    const original = { ...image }, point = array(h.imagePoint(image, .4, .6));
    h.applyImageCrop(image, { x: .2, y: .1, w: .6, h: .8 });
    close(array(h.imagePoint(image, .4, .6)), point);
    h.applyImageCrop(image, { x: 0, y: 0, w: 1, h: 1 });
    close([image.x, image.y, image.w, image.h], [original.x, original.y, original.w, original.h]);
  }
});

test('minimum-size clamping preserves proportions and the anchored corner or center', () => {
  const h = harness();
  for (const corner of ['nw', 'ne', 'sw', 'se']) for (const altKey of [false, true]) {
    const sx = corner.includes('w') ? -1 : 1, sy = corner.includes('n') ? -1 : 1;
    const box = array(h.constrainBox([10, 20, 110, 70], corner, -200 * sx, -100 * sy, { altKey }, true, 4));
    close([box[2] - box[0], box[3] - box[1]], [8, 4]);
    if (altKey) close([(box[0] + box[2]) / 2, (box[1] + box[3]) / 2], [60, 45]);
    else close([sx > 0 ? box[0] : box[2], sy > 0 ? box[1] : box[3]], [sx > 0 ? 10 : 110, sy > 0 ? 20 : 70]);
  }
});

test('quarter turns preserve the center and flips follow the displayed axes', () => {
  const h = harness(), image = { x: 30, y: 40, w: 200, h: 120 };
  h.transformImage(image, 'right');
  close([image.x, image.y, image.w, image.h, image.rotation], [70, 0, 120, 200, 90]);
  h.transformImage(image, 'horizontal'); assert.equal(image.flipY, true);
  h.transformImage(image, 'vertical'); assert.equal(image.flipX, true);
  h.transformImage(image, 'left');
  close([image.x, image.y, image.w, image.h, image.rotation], [30, 40, 200, 120, 0]);
});

test('on-image crop edges map back to source pixels under every rotation and flip', () => {
  const h = harness(false);
  for (const rotation of [0, 90, 180, 270]) for (const flipX of [false, true]) for (const flipY of [false, true]) {
    const image = { x: 30, y: 40, w: 200, h: 120, rotation, flipX, flipY, crop: { x: .1, y: .2, w: .8, h: .6 } };
    const box = [30, 40, 230, 160];
    for (const edge of ['nw', 'n', 'ne', 'w', 'e', 'sw', 's', 'se']) {
      const crop = h.imageCropBox(image, box, edge, edge.includes('w') ? 30 : -30, edge.includes('n') ? 20 : -20, {});
      assert.ok(crop.x >= 0 && crop.y >= 0 && crop.w > 0 && crop.h > 0 && crop.x + crop.w <= 1 && crop.y + crop.h <= 1);
      const draft = { ...image }; h.applyImageCrop(draft, crop);
      close([draft.x, draft.y, draft.w, draft.h], [
        edge.includes('w') ? 60 : 30, edge.includes('n') ? 60 : 40,
        edge.includes('w') || edge.includes('e') ? 170 : 200, edge.includes('n') || edge.includes('s') ? 100 : 120,
      ]);
      const source = [crop.x + crop.w / 2, crop.y + crop.h / 2];
      close(array(h.imagePoint(draft, ...source)), array(h.imagePoint(image, ...source)));
      close(array(h.imageSourcePoint(image, ...h.imagePoint(image, ...source))), source);
    }
  }
});

test('crop handles can restore hidden pixels but cannot invert or leave the original image', () => {
  const h = harness(false);
  for (const rotation of [0, 90, 180, 270]) for (const flipX of [false, true]) for (const flipY of [false, true]) {
    const image = { x: 30, y: 40, w: 100, h: 60, rotation, flipX, flipY, crop: { x: .25, y: .25, w: .5, h: .5 } };
    const box = [30, 40, 130, 100], full = { ...image };
    h.applyImageCrop(full, { x: 0, y: 0, w: 1, h: 1 });
    const nw = { ...image }; h.applyImageCrop(nw, h.imageCropBox(image, box, 'nw', -10000, -10000, {}));
    const restored = { ...image }; h.applyImageCrop(restored, h.imageCropBox(image, [nw.x, nw.y, 130, 100], 'se', 10000, 10000, {}));
    close([restored.x, restored.y, restored.w, restored.h], [full.x, full.y, full.w, full.h]);
    const minimum = { ...image }; h.applyImageCrop(minimum, h.imageCropBox(image, box, 'nw', 10000, 10000, {}));
    close([minimum.w, minimum.h], [4, 4]);
  }
});

test('crop edges use grid snapping unless Ctrl or Command bypasses it', () => {
  const h = harness(), image = { x: 10, y: 20, w: 100, h: 60 }, box = [10, 20, 110, 80];
  for (const [event, right] of [[{}, 96], [{ ctrlKey: true }, 97], [{ metaKey: true }, 97]]) {
    const draft = { ...image }; h.applyImageCrop(draft, h.imageCropBox(image, box, 'e', -13, 0, event));
    close([draft.x + draft.w, draft.y, draft.h], [right, 20, 60]);
  }
});
