/**
 * Rasterises the brand SVGs into the PNGs that platforms will not accept an SVG for, and
 * redraws the Open Graph card around the mark.
 *
 * Drawn with the browser already installed for the end-to-end tests rather than by adding an
 * image toolchain: what ships is what a browser actually renders, which is the only thing that
 * matters for an icon.
 *
 *   node tools/make-brand-assets.mjs
 */
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from '@playwright/test'

const here = path.dirname(fileURLToPath(import.meta.url))
const brand = path.join(here, '..', 'public', 'brand')
const pub = path.join(here, '..', 'public')

const browser = await chromium.launch()

/** Renders an SVG file at a fixed pixel size, on a transparent or filled ground. */
async function raster(svgFile, out, size, background = 'transparent') {
  const svg = fs.readFileSync(path.join(brand, svgFile), 'utf8')
  const page = await browser.newPage({ viewport: { width: size, height: size } })

  await page.setContent(
    `<style>html,body{margin:0;width:${size}px;height:${size}px;background:${background};
     display:grid;place-items:center}svg{width:${size}px;height:${size}px;display:block}</style>${svg}`,
    { waitUntil: 'load' },
  )

  await page.screenshot({ path: path.join(pub, out), omitBackground: background === 'transparent' })
  await page.close()

  console.log(`${out}  ${size}x${size}`)
}

// 32 for the tab on browsers that ignore SVG favicons, 180 for an iOS home screen, 512 for the
// manifest and the install prompt.
await raster('favicon.svg', 'icon-32.png', 32, '#ffffff')
await raster('favicon.svg', 'apple-touch-icon.png', 180, '#ffffff')
await raster('icon-maskable.svg', 'icon-512.png', 512)
await raster('icon-maskable.svg', 'icon-maskable-512.png', 512)

// ---- Open Graph card -----------------------------------------------------

const OG_W = 1200
const OG_H = 630

const card = `
<!doctype html>
<html><head><meta charset="utf-8" /><style>
  * { box-sizing: border-box; margin: 0; }

  body {
    width: ${OG_W}px; height: ${OG_H}px;
    display: flex; flex-direction: column; justify-content: space-between;
    padding: 72px 80px;
    background: #0b0e15; color: #f8f9fb;
    font-family: ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, Arial, sans-serif;
    position: relative; overflow: hidden;
  }

  .mark { display: flex; align-items: center; gap: 16px; }
  .mark span { font-size: 27px; font-weight: 600; letter-spacing: -0.015em; }
  .mark .werk { color: #8fa8f0; }

  h1 { font-size: 76px; font-weight: 600; letter-spacing: -0.032em; line-height: 1.05; max-width: 15ch; }
  p { font-size: 27px; line-height: 1.4; color: #9aa3b4; max-width: 46ch; margin-top: 22px; }

  .ops { display: flex; gap: 10px; flex-wrap: wrap; }
  .op { font-size: 20px; padding: 9px 16px; border: 1px solid #363d4d; border-radius: 999px; color: #d3d8e2; }

  /* The accent, low and wide, so the card reads as a product rather than a poster. */
  .glow {
    position: absolute; inset: auto -180px -420px -180px; height: 620px;
    background: radial-gradient(ellipse at center, rgba(93,127,224,0.30), transparent 62%);
  }
</style></head>
<body>
  <div class="glow"></div>

  <div class="mark">
    <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke-width="1.9"
         stroke-linecap="round" stroke-linejoin="round">
      <path stroke="#8fa8f0" d="M6.4 3.4H3.6v17.2h2.8M17.6 3.4h2.8v17.2h-2.8"/>
      <path stroke="#f1f3f7" d="M8.6 6.4h4.6L15.8 9v9H8.6z"/>
      <path stroke="#f1f3f7" d="M13.2 6.4V9h2.6"/>
    </svg>
    <span>Pdf<span class="werk">Werk</span></span>
  </div>

  <div>
    <h1>PDF operations as an HTTP API</h1>
    <p>Create, edit, merge, split, watermark, protect and summarise — in the browser, over the API, or embedded in your own app.</p>
  </div>

  <div class="ops">
    <div class="op">create</div><div class="op">word → pdf</div><div class="op">edit text</div>
    <div class="op">form fields</div><div class="op">merge</div><div class="op">split</div>
    <div class="op">watermark</div><div class="op">protect</div><div class="op">summarise</div>
  </div>
</body></html>
`

const og = await browser.newPage({ viewport: { width: OG_W, height: OG_H } })
await og.setContent(card, { waitUntil: 'load' })
await og.screenshot({ path: path.join(pub, 'og.png'), type: 'png' })
await og.close()

console.log(`og.png  ${OG_W}x${OG_H}`)

await browser.close()
