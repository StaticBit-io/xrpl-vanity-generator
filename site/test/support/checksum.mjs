import { createHash } from 'node:crypto';
import { BASE58_ALPHABET } from '../../src/js/glyph-field.mjs';

/** True if the string decodes as base58 with a valid double-SHA-256 checksum, as a wallet checks it. */
export function checksumValid(address) {
  let value = 0n;
  for (const character of address) value = value * 58n + BigInt(BASE58_ALPHABET.indexOf(character));
  let hex = value.toString(16);
  if (hex.length % 2) hex = `0${hex}`;
  const bytes = Buffer.concat([Buffer.alloc(address.match(/^r*/)[0].length), Buffer.from(hex, 'hex')]);
  const payload = bytes.subarray(0, -4);
  const hash = (data) => createHash('sha256').update(data).digest();
  return hash(hash(payload)).subarray(0, 4).equals(bytes.subarray(-4));
}
