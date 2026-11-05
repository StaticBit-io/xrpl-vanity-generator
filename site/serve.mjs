// Dev server for site/dist: 127.0.0.1 only, resolving paths the way Cloudflare Pages does.
//
//   node site/serve.mjs
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { dirname, extname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// xrpl-file's dev server starts at 8791; both can run at once.
export const START_PORT = 8792;
const MAX_PORT_ATTEMPTS = 50;
const HOST = '127.0.0.1';

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
  '.xml': 'application/xml; charset=utf-8',
};

const isFile = (path) => stat(path).then((s) => s.isFile(), () => false);
const isDirectory = (path) => stat(path).then((s) => s.isDirectory(), () => false);

async function notFound(root) {
  const file = join(root, '404.html');
  return (await isFile(file)) ? { status: 404, file } : { status: 404 };
}

/** @returns {Promise<{status: number, file?: string, location?: string}>} */
export async function resolveRequest(root, pathname) {
  let decoded;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    return notFound(root);
  }
  const target = join(root, decoded);
  const rel = relative(root, target);
  if (rel.startsWith('..') || isAbsolute(rel)) return notFound(root);
  if (decoded.endsWith('/')) {
    const index = join(target, 'index.html');
    return (await isFile(index)) ? { status: 200, file: index } : notFound(root);
  }
  if (await isFile(target)) return { status: 200, file: target };
  if (await isDirectory(target)) return { status: 301, location: `${pathname}/` };
  return notFound(root);
}

export function createDevServer(root) {
  const siteRoot = resolve(root);
  return createServer(async (request, response) => {
    try {
      const { pathname } = new URL(request.url, `http://${HOST}`);
      const result = await resolveRequest(siteRoot, pathname);
      if (result.location) {
        response.writeHead(result.status, { Location: result.location }).end();
        return;
      }
      if (!result.file) {
        response.writeHead(result.status, { 'Content-Type': TYPES['.txt'] }).end('Not found\n');
        return;
      }
      const type = TYPES[extname(result.file)] ?? 'application/octet-stream';
      response.writeHead(result.status, { 'Content-Type': type, 'Cache-Control': 'no-store' });
      response.end(await readFile(result.file));
    } catch (error) {
      response.writeHead(500).end(`${error.message}\n`);
    }
  });
}

/** Listens on the first free port from `startPort`; resolves with the port actually bound. */
export function listenOnFreePort(server, startPort = START_PORT) {
  return new Promise((resolvePort, reject) => {
    const attempt = (port) => {
      server.once('error', (error) => {
        if (error.code === 'EADDRINUSE' && port - startPort < MAX_PORT_ATTEMPTS) attempt(port + 1);
        else reject(error);
      });
      server.listen(port, HOST, () => resolvePort(port));
    };
    attempt(startPort);
  });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = join(dirname(fileURLToPath(import.meta.url)), 'dist');
  const port = await listenOnFreePort(createDevServer(root));
  console.log(`serving ${root} at http://${HOST}:${port}/`);
}
