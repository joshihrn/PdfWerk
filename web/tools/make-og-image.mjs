/**
 * Renders the Open Graph card to `public/og.png`.
 *
 * Every share preview — Slack, LinkedIn, X, iMessage — points at this one image, and none of
 * those scrapers render SVG, so a raster file has to exist and be committed.
 *
 * Drawn with the browser that is already installed for the end-to-end tests rather than by
 * adding an image library: the card is HTML, so it can use the real design tokens, and what
 * ships is what the page would look like.
 *
 *   node tools/make-og-image.mjs
 */
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from '@playwright/test'

const here = path.dirname(fileURLToPath(import.meta.url))
const out = path.join(here, '..', 'public', 'og.png')

// 1200x630 is what every scraper crops to; anything else gets letterboxed or cut.
const WIDTH = 1200
const HEIGHT = 630

const card = `
<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <style>
      @font-face { font-family: system; src: local('Segoe UI'), local('Helvetica Neue'), local('Arial'); }
      * { box-sizing: border-box; margin: 0; }

      body {
        width: ${WIDTH}px;
        height: ${HEIGHT}px;
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        padding: 72px 80px;
        background: #0b0e15;
        color: #f8f9fb;
        font-family: system, -apple-system, 'Segoe UI', Roboto, Arial, sans-serif;
      }

      .mark { display: flex; align-items: center; gap: 14px; }
      .mark span { font-size: 26px; font-weight: 600; letter-spacing: -0.01em; }

      h1 {
        font-size: 76px;
        font-weight: 600;
        letter-spacing: -0.03em;
        line-height: 1.05;
        max-width: 15ch;
      }

      p { font-size: 27px; line-height: 1.4; color: #9aa3b4; max-width: 46ch; margin-top: 22px; }

      .ops { display: flex; gap: 10px; flex-wrap: wrap; }

      .op {
        font-size: 20px;
        padding: 9px 16px;
        border: 1px solid #363d4d;
        border-radius: 999px;
        color: #d3d8e2;
      }

      /* One accent, low and wide, so the card reads as a product and not a poster. */
      .glow {
        position: absolute;
        inset: auto -180px -420px -180px;
        height: 620px;
        background: radial-gradient(ellipse at center, rgba(93,127,224,0.30), transparent 62%);
      }
    </style>
  </head>
  <body>
    <div class="glow"></div>

    <div class="mark">
      <svg width="30" height="30" viewBox="0 0 16 16" fill="none" stroke="#5d7fe0" stroke-width="1.4">
        <path d="M3.5 1.5h6l3 3v10h-9z" />
        <path d="M9.5 1.5v3h3" />
      </svg>
      <span>PdfWerk</span>
    </div>

    <div>
      <h1>PDF operations as an HTTP API</h1>
      <p>Create, edit, merge, split, watermark, protect and summarise — in the browser, over the API, or embedded in your own app.</p>
    </div>

    <div class="ops">
      <div class="op">create</div>
      <div class="op">word → pdf</div>
      <div class="op">edit text</div>
      <div class="op">form fields</div>
      <div class="op">merge</div>
      <div class="op">split</div>
      <div class="op">watermark</div>
      <div class="op">protect</div>
      <div class="op">summarise</div>
    </div>
  </body>
</html>
`

const browser = await chromium.launch()
const page = await browser.newPage({ viewport: { width: WIDTH, height: HEIGHT }, deviceScaleFactor: 1 })

await page.setContent(card, { waitUntil: 'load' })
await page.screenshot({ path: out, type: 'png' })
await browser.close()

const { size } = fs.statSync(out)
console.log(`wrote ${out} (${(size / 1024).toFixed(1)} KB, ${WIDTH}x${HEIGHT})`)
