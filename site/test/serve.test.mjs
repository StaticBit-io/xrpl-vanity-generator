import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createDevServer, listenOnFreePort, resolveRequest } from '../serve.mjs';

async function tree(t) {
  const parent = await mkdtemp(join(tmpdir(), 'xrpl-vanity-serve-'));
  t.after(() => rm(parent, { recursive: true, force: true }));
  const root = join(parent, 'dist');
  await mkdir(join(root, 'cli'), { recursive: true });
  await writeFile(join(root, 'index.html'), 'home');
  await writeFile(join(root, '404.html'), 'missing');
  await writeFile(join(root, 'cli', 'index.html'), 'cli');
  await writeFile(join(parent, 'secret.txt'), 'secret');
  return root;
}

test('the root and a page folder are served from their index.html', async (t) => {
  const root = await tree(t);
  assert.deepEqual(await resolveRequest(root, '/'), { status: 200, file: join(root, 'index.html') });
  assert.deepEqual(await resolveRequest(root, '/cli/'), { status: 200, file: join(root, 'cli', 'index.html') });
});

test('a page folder without the slash redirects to it, as Pages does', async (t) => {
  const root = await tree(t);
  assert.deepEqual(await resolveRequest(root, '/cli'), { status: 301, location: '/cli/' });
});

test('an unknown path gets the 404 page', async (t) => {
  const root = await tree(t);
  assert.deepEqual(await resolveRequest(root, '/missing/'), { status: 404, file: join(root, '404.html') });
});

test('a path cannot climb out of the root', async (t) => {
  const root = await tree(t);
  assert.equal((await resolveRequest(root, '/../secret.txt')).status, 404);
  assert.equal((await resolveRequest(root, '/%2e%2e/secret.txt')).status, 404);
});

test('the server listens on the loopback address only', async (t) => {
  const root = await tree(t);
  const server = createDevServer(root);
  t.after(() => server.close());
  await listenOnFreePort(server, 18792);
  assert.equal(server.address().address, '127.0.0.1');
});

test('a served page arrives with its content type', async (t) => {
  const root = await tree(t);
  const server = createDevServer(root);
  t.after(() => server.close());
  const port = await listenOnFreePort(server, 18842);
  const response = await fetch(`http://127.0.0.1:${port}/cli/`);
  assert.equal(response.status, 200);
  assert.match(response.headers.get('content-type'), /text\/html/);
  assert.equal(await response.text(), 'cli');
});
