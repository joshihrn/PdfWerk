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
 *   npm run demo            headed and slowed down, for watching
 *   npm run demo:headless   same steps, no window, for CI
 *
 * Run it with `--project=demo`; it is excluded from the default suite so that `npm test` stays
 * about catching regressions rather than re-proving the happy path a third time.
 */

// The package is ESM, so __dirname does not exist here.
const OUTPUT = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'demo-output')

let step = 0

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

  const unpin = await page.addStyleTag({ content: '.app-nav { position: static !important; }' })
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

    // The catalogue is fetched from the running API, so this is already proof of a live server.
    await expect(page.locator('.op').first()).toBeVisible()
    await capture(page, 'landing')
  })

  await test.step('A free API key raises the caller from anonymous to the Free tier', async () => {
    await page.goto('/api')
    await expect(page.getByText('saved in this browser')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Free' })).toBeVisible()

    // Quota is per action and readable without spending any of it.
    await expect(page.getByRole('heading', { name: 'Remaining quota' })).toBeVisible()
    await capture(page, 'api-key-and-quota')
  })

  await test.step('Create a PDF from Markdown', async () => {
    await page.goto('/create')
    await page.getByLabel('Document body').fill(
      '# Quarterly report\n\n' +
        'Revenue rose **twelve per cent** across all regions.\n\n' +
        '## Outlook\n\n' +
        'The board expects the trend to continue into the next quarter.',
    )
    await page.getByLabel('Title').fill('Quarterly report')
    await page.getByRole('button', { name: 'Preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'create-from-text')

    await saveDownload(page, 'quarterly-report.pdf')
  })

  await test.step('Convert a Word document', async () => {
    await page.goto('/word')
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
    await page.setInputFiles('input[type="file"]', {
      name: 'lease.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByLabel('Find').fill('London')
    await page.getByLabel('Replace with').fill('Manchester')
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'edit-text')

    await saveDownload(page, 'edited-text.pdf')
  })

  await test.step('Draw form fields onto a page', async () => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 60_000 })

    const canvas = page.locator('.designer canvas')
    const box = (await canvas.boundingBox())!

    await canvas.click({ position: { x: box.width * 0.15, y: box.height * 0.45 } })
    await page.getByLabel('Name').fill('clientName')
    await page.getByRole('button', { name: 'Done' }).click()

    await canvas.click({ position: { x: box.width * 0.15, y: box.height * 0.55 } })
    await page.getByLabel('Name').fill('signedOn')
    await page.getByRole('button', { name: 'Done' }).click()

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
    await page.setInputFiles('input[type="file"]', path.join(OUTPUT, 'with-form-fields.pdf'))

    await page.getByRole('tab', { name: 'Fill values' }).click()
    await page.getByLabel('clientName').fill('Ada Lovelace')
    await page.getByLabel('signedOn').fill('29 August 2026')
    await page.getByLabel('Flatten').check()

    await page.getByRole('button', { name: 'Fill & preview' }).click()
    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 60_000 })
    await capture(page, 'form-fill')

    await saveDownload(page, 'filled-and-flattened.pdf')
  })

  await test.step('Merge two documents in a chosen order', async () => {
    const first = await makePdf(request, key, 'The first document in the bundle.', 'First')
    const second = await makePdf(request, key, 'The second document in the bundle.', 'Second')

    await page.goto('/merge')
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

    await page.getByLabel('Pages').fill('1-2')
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

    await page.getByLabel('Pages').fill('2')
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

    await page.getByLabel('Text', { exact: true }).fill('DRAFT')
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

    await page.getByLabel('Password to open').fill('correct horse battery staple')

    // No preview is offered, and that is deliberate: a browser cannot render an encrypted PDF,
    // so the interface says so rather than showing an empty frame.
    await expect(page.getByText('Encrypted files cannot preview')).toBeVisible()
    await capture(page, 'pages-protect')

    await saveFormDownload(page, 'protected.pdf')
  })

  await test.step('Summarise a document with an AI model', async () => {
    await page.goto('/summarize')
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

  const produced = fs.readdirSync(OUTPUT)
  console.log(`\nTour complete. ${produced.length} files in ${OUTPUT}`)
  console.log(produced.map((f) => `  ${f}`).join('\n'))
})
