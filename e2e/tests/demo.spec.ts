import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test, type Page } from '@playwright/test'
import { apiKey, configuredProviders, makeDocx, makePdf, multiPagePdf, sharedPdf, signIn } from './support'

/**
 * The guided tour.
 *
 * One test, one page, one continuous take through every feature on its happy path — the run to
 * watch when you want to see what the product does, or to show someone. The other two suites
 * ask "what happens when this goes wrong"; this one only ever asks "does the whole thing work
 * end to end", and it stops at the first thing that does not.
 *
 * It leaves evidence behind. Every step writes a screenshot to `demo-output/`, and the steps
 * that produce a document save the real file through the browser's download path — which is
 * also the only place anything exercises downloading rather than previewing.
 *
 *   npm run demo            headed and paced for watching
 *   npm run demo:headless   same steps, no window, for CI
 *
 * Paced deliberately. Every step puts a caption on screen saying what is about to happen, waits
 * long enough to read it, and types into the short fields a character at a time. `DEMO_PACE`
 * scales all of it — `DEMO_PACE=2` for presenting to a room, `DEMO_PACE=0` to strip the waits
 * out entirely when it is being used as a smoke test.
 *
 * The explicit waits here would be a bad smell in the other two suites, where waiting on a clock
 * instead of on a condition is how flakiness gets in. They are the point of this one: nothing is
 * waiting for the application, it is waiting for the person watching.
 *
 * Run it with `--project=demo`; it is excluded from the default suite so that `npm test` stays
 * about catching regressions rather than re-proving the happy path a third time.
 */

/** Scales every pause. 0 removes them, 2 doubles them. */
const PACE = Number(process.env.DEMO_PACE ?? '1')

/** Long enough to read a caption of a dozen words, at the default pace. */
const READ = 2_200

/** A held moment after something appears, so the eye can land on it. */
const BEAT = 900

const CAPTION_ID = 'pdfwerk-demo-caption'

// The package is ESM, so __dirname does not exist here.
const OUTPUT = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'demo-output')

let step = 0

async function pause(page: Page, ms: number) {
  if (PACE > 0) await page.waitForTimeout(ms * PACE)
}

/**
 * Puts a caption on screen and waits for it to be read.
 *
 * Re-injected every call because a navigation wipes it. It is inert — `pointer-events: none`,
 * `aria-hidden`, and worded so it never collides with anything the tour asserts on — so it
 * narrates without becoming part of what is being demonstrated.
 */
async function narrate(page: Page, heading: string, detail: string) {
  await page.evaluate(
    ({ id, heading, detail }) => {
      let host = document.getElementById(id)

      if (!host) {
        host = document.createElement('div')
        host.id = id
        host.setAttribute('aria-hidden', 'true')
        host.style.cssText = [
          'position:fixed', 'inset:auto 0 0 0', 'z-index:2147483647',
          'padding:18px 28px', 'pointer-events:none',
          'font:15px/1.45 ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif',
          'color:#fff', 'background:linear-gradient(transparent,rgba(11,14,21,.92) 38%)',
          'transition:opacity .25s ease',
        ].join(';')
        document.body.appendChild(host)
      }

      host.innerHTML =
        `<div style="max-width:1100px;margin:0 auto">
           <div style="font-weight:600;font-size:17px;letter-spacing:-.01em">${heading}</div>
           <div style="opacity:.75;margin-top:3px">${detail}</div>
         </div>`
    },
    { id: CAPTION_ID, heading, detail },
  )

  await pause(page, READ)
}

/**
 * Types visibly, the way a person would, rather than pasting the value in one go.
 *
 * Cleared first: unlike fill(), pressSequentially appends. A field carrying a default — the page
 * range starts at "all" — would otherwise end up holding "all1-2" and be rejected as malformed.
 */
async function typeInto(locator: ReturnType<Page['getByLabel']>, text: string) {
  await locator.fill('')
  await locator.click()
  await locator.pressSequentially(text, { delay: PACE > 0 ? 45 * PACE : 0 })
}

/**
 * Numbered so the folder reads in the order the tour ran, whatever the filesystem thinks.
 *
 * The sticky nav is pinned to static for the duration of the shot. A full-page capture composites
 * a taller viewport than the browser really has, and a sticky element lands wherever it happened
 * to be sitting — which puts the navigation bar across the middle of the page. Harmless on screen,
 * but these images are meant to be shown to people.
 */
async function capture(page: Page, name: string) {
  step += 1

  // The caption goes too. It belongs in the recording, where it sits at the foot of the window
  // and narrates; in a full-page capture it composites across the middle of the page like any
  // other fixed element, and these images are meant to be clean product shots.
  const unpin = await page.addStyleTag({
    content: `.app-nav { position: static !important; } #${CAPTION_ID} { display: none !important; }`,
  })

  const file = path.join(OUTPUT, `${String(step).padStart(2, '0')}-${name}.png`)

  await page.screenshot({ path: file, fullPage: true })
  await unpin.evaluate((el) => el.remove())
}

/**
 * Saves the document currently in the result pane.
 *
 * Scoped to the result bar on purpose. Once a result exists there are two Download buttons: the
 * form's, which re-runs the whole operation and spends quota again, and this one, which hands
 * over the document already on screen. For a walkthrough the second is the honest one — it is
 * what a person would actually click, having just looked at the preview.
 */
async function saveDownload(page: Page, saveAs: string) {
  const download = page.waitForEvent('download', { timeout: 60_000 })
  await page.locator('.result__bar').getByRole('button', { name: 'Download' }).click()

  const file = await download
  await file.saveAs(path.join(OUTPUT, saveAs))

  // A download that arrives empty still fires the event, so the size is the real assertion.
  const { size } = fs.statSync(path.join(OUTPUT, saveAs))
  expect(size, `${saveAs} should not be empty`).toBeGreaterThan(500)
}

/**
 * Saves straight from the form, for an operation that never produces a preview to save from.
 * Encryption is the only one: a browser cannot render an encrypted PDF.
 */
async function saveFormDownload(page: Page, saveAs: string) {
  const download = page.waitForEvent('download', { timeout: 60_000 })
  await page.getByRole('button', { name: 'Download', exact: true }).click()

  const file = await download
  await file.saveAs(path.join(OUTPUT, saveAs))

  const { size } = fs.statSync(path.join(OUTPUT, saveAs))
  expect(size, `${saveAs} should not be empty`).toBeGreaterThan(500)
}

test('a guided tour of every feature', async ({ page, request }) => {
  // Long by design: this is fifteen operations in sequence, one of them a model round trip.
  test.setTimeout(10 * 60_000)

  fs.rmSync(OUTPUT, { recursive: true, force: true })
  fs.mkdirSync(OUTPUT, { recursive: true })

  const key = await apiKey(request)
  await signIn(page, key)

  await test.step('The landing page explains what the service does', async () => {
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'PDF operations as an HTTP API' })).toBeVisible()
    await narrate(page, 'PdfWerk', 'Eleven PDF operations, each one an HTTP endpoint. Here is the catalogue.')

    // The catalogue is fetched from the running API, so this is already proof of a live server.
    await expect(page.locator('.op').first()).toBeVisible()
    await capture(page, 'landing')
  })

  await test.step('A free API key raises the caller from anonymous to the Free tier', async () => {
    await page.goto('/api')
    await narrate(page, 'A free key, no signup', 'One request raises you from anonymous to the Free tier. No account, no email.')
    await expect(page.getByText('saved in this browser')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Free' })).toBeVisible()

    // Quota is per action and readable without spending any of it.
    await expect(page.getByRole('heading', { name: 'Remaining quota' })).toBeVisible()
    await capture(page, 'api-key-and-quota')
  })

  await test.step('Create a PDF from Markdown', async () => {
    await page.goto('/create')
    await narrate(page, 'Create a PDF from text', 'Write Markdown, get a paginated document back.')
    await page.getByLabel('Document body').fill(
      '# Quarterly report\n\n' +
        'Revenue rose **twelve per cent** across all regions.\n\n' +
        '## Outlook\n\n' +
        'The board expects the trend to continue into the next quarter.',
    )
    await typeInto(page.getByLabel('Title'), 'Quarterly report')
    await pause(page, BEAT)
    await page.getByRole('button', { name: 'Preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'create-from-text')

    await saveDownload(page, 'quarterly-report.pdf')
  })

  await test.step('Convert a Word document', async () => {
    await page.goto('/word')
    await narrate(page, 'Word to PDF', 'A .docx goes in. LibreOffice converts it where installed, a managed converter otherwise.')
    await page.setInputFiles('input[type="file"]', {
      name: 'report.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: makeDocx(),
    })
    await page.getByRole('button', { name: 'Convert & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'word-to-pdf')

    await saveDownload(page, 'converted-from-word.pdf')
  })

  await test.step('Replace text inside an existing PDF', async () => {
    const pdf = await makePdf(
      request,
      key,
      'This agreement covers London and the surrounding counties for the full term of the lease.',
      'Lease',
    )

    await page.goto('/edit')
    await narrate(page, 'Change the words in a finished PDF', 'The old text is removed, not covered over, so it stops being searchable too.')
    await page.setInputFiles('input[type="file"]', {
      name: 'lease.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await typeInto(page.getByLabel('Find'), 'London')
    await typeInto(page.getByLabel('Replace with'), 'Manchester')
    await pause(page, BEAT)
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'edit-text')

    await saveDownload(page, 'edited-text.pdf')
  })

  await test.step('Draw form fields onto a page', async () => {
    await page.goto('/forms')
    await narrate(page, 'Draw form fields onto the page', 'Click where a field belongs. Coordinates are sent in PDF points, exactly as placed.')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 60_000 })
    await pause(page, BEAT)

    const canvas = page.locator('.designer canvas')
    const box = (await canvas.boundingBox())!

    await canvas.click({ position: { x: box.width * 0.15, y: box.height * 0.45 } })
    await typeInto(page.getByLabel('Name'), 'clientName')
    await page.getByRole('button', { name: 'Done' }).click()
    await pause(page, BEAT)

    await canvas.click({ position: { x: box.width * 0.15, y: box.height * 0.55 } })
    await typeInto(page.getByLabel('Name'), 'signedOn')
    await page.getByRole('button', { name: 'Done' }).click()
    await pause(page, BEAT)

    await expect(page.locator('.field-box')).toHaveCount(2)
    await expect(page.getByText('2 field(s)')).toBeVisible()
    await capture(page, 'form-designer')

    // Placing fields only draws them; applying is what writes them into the document.
    await page.getByRole('button', { name: 'Apply & preview' }).click()
    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })

    await saveDownload(page, 'with-form-fields.pdf')
  })

  await test.step('Fill those fields and flatten them into the page', async () => {
    // Uploads the document produced by the previous step, so the tour genuinely builds on itself.
    await page.goto('/forms')
    await narrate(page, 'Now fill the form that was just drawn', 'This uploads the document from the previous step. The tour builds on itself.')
    await page.setInputFiles('input[type="file"]', path.join(OUTPUT, 'with-form-fields.pdf'))

    await page.getByRole('tab', { name: 'Fill values' }).click()
    await typeInto(page.getByLabel('clientName'), 'Ada Lovelace')
    await typeInto(page.getByLabel('signedOn'), '29 August 2026')
    await page.getByLabel('Flatten').check()
    await pause(page, BEAT)

    await page.getByRole('button', { name: 'Fill & preview' }).click()
    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'form-fill')

    await saveDownload(page, 'filled-and-flattened.pdf')
  })

  await test.step('Merge two documents in a chosen order', async () => {
    const first = await makePdf(request, key, 'The first document in the bundle.', 'First')
    const second = await makePdf(request, key, 'The second document in the bundle.', 'Second')

    await page.goto('/merge')
    await narrate(page, 'Merge, in the order you choose', 'Sequence is the whole point, so the list can be reordered before it is sent.')
    await page.setInputFiles('input[type="file"]', [
      { name: 'first.pdf', mimeType: 'application/pdf', buffer: first },
      { name: 'second.pdf', mimeType: 'application/pdf', buffer: second },
    ])

    // Sequence is the whole point of merging, so the tour reorders before submitting.
    await page.getByRole('button', { name: 'Move second.pdf earlier' }).click()
    await expect(page.locator('.file__name')).toHaveText(['second.pdf', 'first.pdf'])

    await page.getByRole('button', { name: 'Merge & preview' }).click()
    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'merge')

    await saveDownload(page, 'merged.pdf')
  })

  await test.step('Extract a range of pages', async () => {
    await page.goto('/pages')
    await page.setInputFiles('input[type="file"]', {
      name: 'long.pdf',
      mimeType: 'application/pdf',
      buffer: await multiPagePdf(request),
    })

    await narrate(page, 'Take just the pages you want', 'Ranges understand 1-3, odd, even, open-ended tails, and all.')
    await typeInto(page.getByLabel('Pages'), '1-2')
    await pause(page, BEAT)
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'pages-extract')
  })

  await test.step('Rotate a single page', async () => {
    await page.goto('/pages')
    await page.getByRole('tab', { name: 'Rotate' }).click()
    await page.setInputFiles('input[type="file"]', {
      name: 'long.pdf',
      mimeType: 'application/pdf',
      buffer: await multiPagePdf(request),
    })

    await narrate(page, 'Rotate a single page', 'Only the pages named turn. The rest are left exactly as they were.')
    await typeInto(page.getByLabel('Pages'), '2')
    await pause(page, BEAT)
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'pages-rotate')
  })

  await test.step('Stamp a watermark across every page', async () => {
    await page.goto('/pages')
    await page.getByRole('tab', { name: 'Watermark' }).click()
    await page.setInputFiles('input[type="file"]', {
      name: 'long.pdf',
      mimeType: 'application/pdf',
      buffer: await multiPagePdf(request),
    })

    await narrate(page, 'Stamp a watermark', 'Drawn beneath the content by default, so the document stays readable.')
    await typeInto(page.getByLabel('Text', { exact: true }), 'DRAFT')
    await pause(page, BEAT)
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'pages-watermark')

    await saveDownload(page, 'watermarked.pdf')
  })

  await test.step('Encrypt a document with a password', async () => {
    await page.goto('/pages')
    await page.getByRole('tab', { name: 'Protect' }).click()
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await narrate(page, 'Encrypt with a password', 'No preview is offered here, because a browser cannot render an encrypted PDF.')
    await typeInto(page.getByLabel('Password to open'), 'correct horse battery staple')
    await pause(page, BEAT)

    // No preview is offered, and that is deliberate: a browser cannot render an encrypted PDF,
    // so the interface says so rather than showing an empty frame.
    await expect(page.getByText('Encrypted files cannot preview')).toBeVisible()
    await capture(page, 'pages-protect')

    await saveFormDownload(page, 'protected.pdf')
  })

  await test.step('Summarise a document with an AI model', async () => {
    await page.goto('/summarize')
    await narrate(page, 'Summarise with a model', 'Gemini, Groq or a local Ollama, whichever is configured. Nothing is sent without one.')
    await page.setInputFiles('input[type="file"]', {
      name: 'report.pdf',
      mimeType: 'application/pdf',
      buffer: await multiPagePdf(request),
    })

    await page.getByRole('button', { name: 'Summarise' }).click()

    if ((await configuredProviders(request)).length === 0) {
      // No key is committed anywhere, so a fresh clone lands here. The tour still shows what a
      // caller without a model configured would see, rather than pretending the step ran.
      await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 120_000 })
      await capture(page, 'summarise-no-model-configured')
      return
    }

    await expect(page.locator('.lede')).toBeVisible({ timeout: 120_000 })
    await capture(page, 'summarise')
  })

  await test.step('Inspect a document and read its structure back', async () => {
    await page.goto('/inspect')
    await narrate(page, 'Read a document back', 'The fields designed a few steps ago come back by name. That round trip is the whole game.')
    await page.setInputFiles('input[type="file"]', path.join(OUTPUT, 'with-form-fields.pdf'))
    await page.getByRole('button', { name: 'Inspect' }).click()

    // The fields designed earlier in the tour come back by name, which is the round trip that
    // matters most: what the designer wrote, a reader can find.
    await expect(page.getByText('clientName')).toBeVisible({ timeout: 60_000 })
    await expect(page.getByText('signedOn')).toBeVisible()
    await capture(page, 'inspect')
  })

  await test.step('Drop the same tools into someone else\'s page with one script tag', async () => {
    await page.goto('/embed-demo.html')
    await narrate(page, 'The same tools, dropped into another page', 'One script tag, one mount call. Rendered in a shadow root so nothing leaks either way.')

    for (const widget of ['Create a PDF', 'Merge PDFs', 'Inspect a PDF', 'Fill a PDF form']) {
      await expect(page.getByText(widget, { exact: true }).first()).toBeVisible()
    }

    // Driven for real, not just rendered: the embedded widget produces a document of its own.
    const host = page.locator('#pdf-create')
    await host.getByRole('textbox').first().fill('# Embedded\n\nProduced by the drop-in widget.')
    await host.getByRole('button', { name: 'Create PDF' }).click()

    await expect(page.getByText(/create →/)).toBeVisible({ timeout: 60_000 })
    await capture(page, 'embed-widget')
  })

  await narrate(page, 'That is the whole product', 'Every screenshot and document from this run is waiting in demo-output/.')
  await pause(page, BEAT)

  const produced = fs.readdirSync(OUTPUT)
  console.log(`\nTour complete. ${produced.length} files in ${OUTPUT}`)
  console.log(produced.map((f) => `  ${f}`).join('\n'))
})
