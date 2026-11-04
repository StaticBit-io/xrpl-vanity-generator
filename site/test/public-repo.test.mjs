import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../..', import.meta.url));
const readme = readFileSync(join(root, 'README.md'), 'utf8');

function textFiles(folder) {
  return readdirSync(join(root, folder), { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && /\.(md|html|txt|yml)$/.test(entry.name))
    .map((entry) => join(entry.parentPath, entry.name));
}

test('the documents name no local machine and no legal entity', () => {
  const files = [join(root, 'README.md'), ...textFiles('docs'), ...textFiles('site/src')];
  assert.ok(files.length > 20);
  const found = files.filter((file) => /Evril|[A-Z]:[\\/](GIT|Claude|Lab|!Git)\b|SBP Labs/.test(readFileSync(file, 'utf8')));
  assert.deepEqual(found, []);
});

test('the README builds without the MAUI workload', () => {
  assert.doesNotMatch(readme, /^dotnet build -c Release$/m);
  assert.match(readme, /dotnet run --project src\/XrplVanity\.Cli -c Release --/);
  assert.match(readme, /XrplVanity\.Portable\.slnf/);
});

test('the README explains the short name in its examples', () => {
  assert.match(readme, /`xrpl-vanity` stands for `dotnet run --project src\/XrplVanity\.Cli -c Release --`/);
});

test('the README shows one value for the flags that take one', () => {
  assert.doesNotMatch(readme, /`--(starts|ends|contains) P\.\.\.`/);
});

test('the README names no word list the repository lacks', () => {
  assert.doesNotMatch(readme, /google20kwords/);
});

test('the README keeps no superseded speed', () => {
  assert.doesNotMatch(readme, /13\.5 million|~19\.4|~12\.1M\/s|~10\.7M\/s/);
});
