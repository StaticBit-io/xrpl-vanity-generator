import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const workflow = read('../../.github/workflows/site.yml');
const wrangler = read('../wrangler.toml');

test('wrangler names the project and deploys the build output', () => {
  assert.match(wrangler, /^name = "xrpl-vanity-site"$/m);
  assert.match(wrangler, /^pages_build_output_dir = "\.\/dist"$/m);
});

test('the workflow deploys the project wrangler names', () => {
  assert.match(workflow, /PROJECT: xrpl-vanity-site/);
});

test('a red suite never goes live', () => {
  const job = workflow.slice(workflow.indexOf('deploy-site:'));
  assert.match(job, /needs: test/);
});

test('only a push to main deploys', () => {
  const job = workflow.slice(workflow.indexOf('deploy-site:'));
  assert.match(job, /github\.event_name == 'push'/);
  assert.match(job, /github\.ref == 'refs\/heads\/main'/);
});

test('nothing deploys while the repository is private, since the site links it', () => {
  const job = workflow.slice(workflow.indexOf('deploy-site:'));
  assert.match(job.split('\n').find((line) => line.includes('if:')), /!github\.event\.repository\.private/);
});

test('wrangler is pinned, and the secrets come from the repository', () => {
  assert.match(workflow, /wrangler@\d+\.\d+\.\d+/);
  assert.doesNotMatch(workflow, /wrangler@latest|npx --yes wrangler(?!@)/);
  assert.match(workflow, /CLOUDFLARE_API_TOKEN: \$\{\{ secrets\.CLOUDFLARE_API_TOKEN \}\}/);
  assert.match(workflow, /CLOUDFLARE_ACCOUNT_ID: \$\{\{ secrets\.CLOUDFLARE_ACCOUNT_ID \}\}/);
});

test('deploys run in order, never cancelling one another', () => {
  assert.match(workflow, /cancel-in-progress: false/);
});
