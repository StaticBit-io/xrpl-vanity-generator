import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const workflow = read('../../.github/workflows/site.yml');
const wrangler = read('../wrangler.toml');
const deployJob = workflow.slice(workflow.indexOf('deploy-site:'));

test('wrangler names the project and deploys the build output', () => {
  assert.match(wrangler, /^name = "xrpl-vanity-site"$/m);
  assert.match(wrangler, /^pages_build_output_dir = "\.\/dist"$/m);
});

test('the workflow deploys the project wrangler names', () => {
  assert.match(workflow, /PROJECT: xrpl-vanity-site/);
});

test('a red suite never goes live', () => {
  assert.match(deployJob, /needs: test/);
});

test('only a manual run deploys', () => {
  assert.match(workflow, /^ {2}workflow_dispatch:/m);
  assert.match(deployJob.split('\n').find((line) => line.includes('if:')), /github\.event_name == 'workflow_dispatch'/);
});

test('a manual run offers exactly dev and main', () => {
  const input = workflow.slice(workflow.indexOf('workflow_dispatch:'), workflow.indexOf('jobs:'));
  assert.match(input, /required: true/);
  assert.match(input, /type: choice/);
  assert.deepEqual([...input.matchAll(/^ +- (\S+)$/gm)].map((match) => match[1]), ['dev', 'main']);
});

test('the chosen branch is what gets tested, built and uploaded', () => {
  const testJob = workflow.slice(workflow.indexOf('  test:'), workflow.indexOf('deploy-site:'));
  assert.match(testJob, /ref: \$\{\{ inputs\.branch \|\| github\.ref \}\}/);
  assert.match(deployJob, /ref: \$\{\{ inputs\.branch \}\}/);
  assert.match(deployJob, /pages deploy --branch "\$BRANCH"/);
  assert.doesNotMatch(deployJob, /--branch main/);
});

test('the commit recorded is the deployed one, not the commit the run started from', () => {
  assert.doesNotMatch(deployJob, /GITHUB_SHA/);
  assert.match(deployJob, /--commit-hash "\$\(git rev-parse HEAD\)"/);
});

test('the input reaches the shell through the environment', () => {
  assert.match(deployJob, /BRANCH: \$\{\{ inputs\.branch \}\}/);
  assert.doesNotMatch(deployJob, /run:.*\$\{\{/);
});

test('both long-lived branches are tested on push', () => {
  assert.match(workflow, /branches: \[dev, main\]/);
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
