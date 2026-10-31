const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const source = readFileSync(join(__dirname, 'wwwroot', 'worker.js'), 'utf8');
const handler = source.slice(source.indexOf('self.onmessage ='), source.indexOf('  self.postMessage({ ready:'));
function harness(request) {
  const replies = [];
  const self = {
    location: { href: 'https://editor.test/worker.js' },
    postMessage: (reply, transfer) => replies.push({ reply, transfer }),
  };
  vm.runInNewContext(handler, {
    self, exports: { BrowserEditor: { RequestBytes: request } },
    URL, Blob, Uint8Array, performance,
    base64: () => { throw new Error('Binary protocol must not base64-encode commands'); },
  });
  return {
    replies,
    send: (body, method = 'POST') => self.onmessage({
      data: { id: 7, url: '/api/page/0/items?v=2', method, body, fileName: 'input.pdf' },
    }),
  };
}

test('raw JSON reaches RequestBytes unchanged and response bytes are transferred, not encoded', async () => {
  const bytes = new Uint8Array([0, 255, 13]);
  const body = '{"text":"Caf\\u00e9"}';
  const h = harness((method, path, query, text, exchange, fileName) => {
    assert.equal(method, 'POST');
    assert.equal(path, '/api/page/0/items');
    assert.deepEqual(JSON.parse(query), { v: '2' });
    assert.equal(text, body);
    assert.equal(fileName, 'input.pdf');
    exchange.contentType = 'application/json';
    exchange.performance = '{"rasterMs":2}';
    return bytes;
  });
  await h.send(body);
  const { reply, transfer } = h.replies[0];
  assert.equal(reply.reply.bytes, bytes);
  assert.equal(reply.reply.performance.rasterMs, 2);
  assert.equal(transfer.length, 1);
  assert.equal(transfer[0], bytes.buffer);
});

test('Blob upload travels as exact binary bytes with no base64 command body', async () => {
  const h = harness((method, path, query, body, exchange) => {
    assert.equal(body, null);
    assert.deepEqual([...exchange.bytes], [0, 255, 13]);
    exchange.contentType = 'application/json';
    return new Uint8Array([123, 125]);
  });
  await h.send(new Blob([new Uint8Array([0, 255, 13])]));
  const { timings } = h.replies[0].reply;
  assert.equal(timings.binaryUpload, true);
  assert.equal(timings.uploadBytes, 3);
  assert.equal(timings.uploadEncodeMs, 0);
});

test('managed errors remain errors and a subsequent command still succeeds', async () => {
  let fail = true;
  const h = harness((method, path, query, body, exchange) => {
    if (fail) { exchange.error = '{"error":"invalid PDF"}'; return new Uint8Array(); }
    exchange.contentType = 'application/json';
    return new Uint8Array([123, 125]);
  });
  await h.send(null);
  assert.equal(h.replies[0].reply.reply.error, 'invalid PDF');
  assert.equal(h.replies[0].transfer, undefined);
  fail = false;
  await h.send(null);
  assert.equal(h.replies[1].transfer.length, 1);
});
