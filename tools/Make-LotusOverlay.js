// Generates src/Lotus.Infrastructure/Integrations/Lotus.ovl (RivaTuner OverlayEditor layout).
// Units: negative PositionX/Y and ExtentX/Y are character cells (as in RTSS samples).
// ExtentOrigin: 0 top-left, 3 middle-left, 5 middle-right, 4 center.
const fs = require('fs');
const out = process.argv[2];

const ROSE = 'F27BA8', GOLD = 'EEC1A8', TEXT = 'F8F0F3', MUTED = 'A88E9C', CARD = 'E60E0709', PANEL = '80281520', LINE = 'C0E8378A';
const hot = (src, limit) => `${GOLD},,${limit},FF5A5A,${limit},(${src},0)`; // gold, red from limit

const sources = [
  ['CPU usage', '%', '', ''], ['CPU temperature', '°C', '', ''], ['CPU clock', 'GHz', '%.1f', 'x/1000'],
  ['GPU1 usage', '%', '', ''], ['GPU1 temperature', '°C', '', ''], ['GPU1 clock', 'GHz', '%.2f', 'x/1000'],
  ['GPU1 memory usage percent', '%', '', ''], ['GPU1 memory usage', 'GB', '%.1f', 'x/1024'], ['GPU1 total memory', 'GB', '%.0f', 'x/1024'],
  ['RAM usage percent', '%', '', ''], ['RAM usage', 'GB', '%.1f', 'x/1024'], ['Total RAM', 'GB', '%.0f', 'x/1024'],
  // Live statistics over the internal frametime ring buffer (last 1024 frames); the <FRAVG>/<FR10L> tags only work in benchmark mode.
  ['FPS avg', 'FPS', '%.0f', '1000/swavg(Frametime,0)', 'Stub'],
  ['FPS low1', 'FPS', '%.0f', '1000/percentile(Frametime,99)', 'Stub'],
  ['FPS low01', 'FPS', '%.0f', '1000/percentile(Frametime,99.9)', 'Stub'],
];

const W = 32; // card width in cells
const layers = [];
const L = (name, text, x, y, w, h, o) => layers.push({ name, text, x, y, w, h, origin: 0, ...o });

L('Card', '', 0, 0, W, 17, { bg: CARD });
L('Header band', '', 0, 0, W, 1, { bg: '60E8378A' });
L('Brand', 'LOTUS', 1, 0, 10, 1, { origin: 3, color: TEXT, size: 100 });
L('Tagline', 'PLAY BETTER', W - 12, 0, 11, 1, { origin: 5, color: GOLD, size: 75 });

// FPS block
L('FPS caption', 'FPS', 1, 1, 6, 1, { origin: 3, color: ROSE, size: 75 });
L('FPS value', '<FR>', 1, 2, 13, 3, { origin: 3, color: TEXT, size: 300 });
L('Stats panel', '', 15, 1, W - 16, 4, { bg: PANEL });
[['AVG', 'FPS avg', 1.5], ['1% LOW', 'FPS low1', 2.5], ['0.1% LOW', 'FPS low01', 3.5]].forEach(([cap, src], i) => {
  L(cap + ' caption', cap, 16, 1 + i + (i ? 0 : 0), 8, 1, { origin: 3, color: MUTED, size: 75 });
  L(cap + ' value', `%${src}%`, 23, 1 + i, W - 25, 1, { origin: 5, color: i === 0 ? TEXT : GOLD });
});
L('Stats units', 'FPS', 23, 4, W - 25, 1, { origin: 5, color: MUTED, size: 50 });

// Frametime
L('Frametime caption', 'FRAMETIME', 1, 5, 10, 1, { origin: 3, color: ROSE, size: 75 });
L('Frametime value', '<FT> ms', W - 12, 5, 11, 1, { origin: 5, color: TEXT });
L('Frametime panel', '', 1, 6, W - 2, 3, { bg: PANEL });
L('Frametime graph', '<G=Frametime,0,0,1,0,50,10>', 1, 6, W - 2, 3, { color: '80F27BA8' });
L('Frametime line', '<G=Frametime,0,0,1,0,50,0>', 1, 6, W - 2, 3, { color: LINE });

// Hardware rows: label | value % | usage history | temperature | clock or GB
const rows = [
  ['CPU', 'CPU usage', 'CPU temperature', '%CPU clock% GHz'],
  ['GPU', 'GPU1 usage', 'GPU1 temperature', '%GPU1 clock% GHz'],
  ['VRAM', 'GPU1 memory usage percent', null, '%GPU1 memory usage% / %GPU1 total memory% GB'],
  ['RAM', 'RAM usage percent', null, '%RAM usage% / %Total RAM% GB'],
];
rows.forEach(([label, usage, temp, right], i) => {
  const y = 10 + i * 1.5 | 0, row = 10 + i;
  L(label + ' label', label, 1, row, 5, 1, { origin: 3, color: ROSE });
  L(label + ' usage', `%${usage}%`, 6, row, 4, 1, { origin: 5, color: TEXT });
  L(label + ' usage units', '" %"', 10, row, 2, 1, { origin: 3, color: MUTED, size: 75 });
  L(label + ' history bg', '', 12, row, 7, 1, { bg: PANEL });
  L(label + ' history', `<G=${usage},0,0,1,0,100,10>`, 12, row, 7, 1, { color: 'C0E8378A' });
  if (temp) {
    L(label + ' temperature', `%${temp}%°C`, 19, row, 5, 1, { origin: 5, color: hot(temp, 80) });
    L(label + ' clock', right, 24, row, W - 25, 1, { origin: 5, color: MUTED, size: 75 });
  } else {
    L(label + ' amount', right, 19, row, W - 20, 1, { origin: 5, color: GOLD, size: 75 });
  }
});

// Footer
L('Footer line', '', 1, 15, W - 2, 1, { bg: '30F27BA8', h: 1 });
L('API and resolution', '<API>  |  <RES>', 1, 15, 20, 1, { origin: 3, color: MUTED, size: 75 });
L('Footer frametime', '<FT> ms', W - 10, 15, 9, 1, { origin: 5, color: MUTED, size: 75 });
L('Cursor position placeholder', '" \\b"', 0, 17, 0, 0, {});

let s = `[Master]\r\nImplementation=2\r\nFontFace=Segoe UI\r\nFontHeight=-13\r\nFontWeight=600\r\nZoomRatio=2\r\n` +
  `[Settings]\r\nName=Lotus\r\nRefreshPeriod=500\r\nLockUserSettings=1\r\nEmbeddedImage=\r\nPingAddr=\r\n` +
  `[General]\r\nSources=${sources.length}\r\nTables=0\r\nLayers=${layers.length}\r\n`;
sources.forEach(([name, units, format, formula, id], i) => {
  s += `[Source${i}]\r\nName=${name}\r\nUnits=${units}\r\nFormat=${format}\r\nFormula=${formula}\r\nProvider=HAL\r\nID=${id ?? name}\r\n`;
});
layers.forEach((l, i) => {
  s += `[Layer${i}]\r\nName=${l.name}\r\nText=${l.text}\r\nPositionX=${-l.x}\r\nPositionY=${-l.y}\r\nExtentX=${-l.w}\r\nExtentY=${-l.h}\r\nExtentOrigin=${l.origin}\r\nFixedAlignment=1\r\n`;
  if (l.size) s += `Size=${l.size}\r\n`;
  if (l.color) s += `TextColor=${l.color}\r\n`;
  if (l.bg) s += `BgndColor=${l.bg}\r\n`;
});
fs.writeFileSync(out, s, 'utf8');
console.log(`${sources.length} sources, ${layers.length} layers`);
