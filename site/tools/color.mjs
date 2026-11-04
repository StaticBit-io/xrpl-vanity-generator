// OKLCH -> sRGB and WCAG 2.x contrast, shared by the contrast report and its test.

/** Parses `oklch(L C H)` (L as a fraction or a percentage) or `#rrggbb` into linear-light sRGB. */
export function parseColor(text) {
  const value = text.trim();
  const hex = /^#([0-9a-f]{6})$/i.exec(value);
  if (hex) {
    const n = parseInt(hex[1], 16);
    return { rgb: [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => c / 255), inGamut: true };
  }
  const match = /^oklch\(\s*([\d.]+)(%?)\s+([\d.]+)\s+([\d.]+)\s*\)$/i.exec(value);
  if (!match) throw new Error(`unsupported colour: ${text}`);
  const lightness = Number(match[1]) / (match[2] ? 100 : 1);
  return oklchToSrgb(lightness, Number(match[3]), Number(match[4]));
}

export function oklchToSrgb(lightness, chroma, hueDegrees) {
  const h = (hueDegrees * Math.PI) / 180;
  const a = chroma * Math.cos(h);
  const b = chroma * Math.sin(h);
  const l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (lightness - 0.0894841775 * a - 1.291485548 * b) ** 3;
  const linear = [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ];
  const inGamut = linear.every((c) => c >= -0.0005 && c <= 1.0005);
  const encoded = linear.map((c) => {
    const clamped = Math.min(1, Math.max(0, c));
    return clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * clamped ** (1 / 2.4) - 0.055;
  });
  return { rgb: encoded, inGamut };
}

export function toHex({ rgb }) {
  return `#${rgb.map((c) => Math.round(c * 255).toString(16).padStart(2, '0')).join('')}`;
}

export function luminance({ rgb }) {
  const [r, g, b] = rgb.map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(foreground, background) {
  const a = luminance(foreground);
  const b = luminance(background);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/** Composites a translucent colour (`rgba(r,g,b,a)`) over an opaque background. */
export function over(foregroundRgb, alpha, background) {
  return { rgb: foregroundRgb.map((c, i) => c * alpha + background.rgb[i] * (1 - alpha)), inGamut: true };
}
