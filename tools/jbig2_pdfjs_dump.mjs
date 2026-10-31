// Dumps the decoded first-page image of each PDF as a '0'/'1' string (1 = white) using pdf.js, for tools/jbig2_crosscheck.py.
// Run from a directory whose node_modules contains pdfjs-dist (3.x legacy build recommended: later versions hand images to a canvas):
//   node jbig2_pdfjs_dump.mjs file.pdf...
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
const require = createRequire(path.join(process.cwd(), "noop.js"));
const pdfjs = require("pdfjs-dist/legacy/build/pdf.js");
const out = {};
for (const file of process.argv.slice(2)) {
  const name = path.basename(file);
  try {
    const doc = await pdfjs.getDocument({ data: new Uint8Array(fs.readFileSync(file)), verbosity: 0, isEvalSupported: false }).promise;
    const page = await doc.getPage(1);
    const ops = await page.getOperatorList();
    const idx = ops.fnArray.indexOf(pdfjs.OPS.paintImageXObject);
    if (idx < 0) throw new Error("no image op");
    const id = ops.argsArray[idx][0];
    const img = await new Promise((res) => page.objs.get(id, res));
    const { width, height, data, kind } = img;
    const stride = (width + 7) >> 3;
    let s = "";
    for (let y = 0; y < height; y++)
      for (let x = 0; x < width; x++) {
        let v;
        if (kind === 1) v = (data[y * stride + (x >> 3)] >> (7 - (x & 7))) & 1 ? 255 : 0;
        else v = data[(y * width + x) * (kind === 2 ? 3 : 4)];
        s += v > 127 ? "1" : "0";
      }
    out[name] = s;
  } catch (e) {
    out[name] = "ERR " + String(e.stack || e).slice(0, 200).replace(/\s+/g, " ");
  }
}
process.stdout.write("\n@@JSON@@" + JSON.stringify(out) + "\n");
