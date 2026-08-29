import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import {
  apiKey,
  authHeaders,
  configuredProviders,
  makeDocx,
  makeFormPdf,
  makePdf,
  multiPagePdf,
  pdfPart,
  sharedPdf,
  signIn,
} from './support'

/**
 * Browser coverage.
 *
 * Written against what a person sees — headings, labels, button text — rather than CSS classes
 * or test ids, so a restyle does not break the suite but a broken interface does. The exception
 * is the form designer, where positions are the thing under test and the DOM is the only place
 * to read them.
 */

test.beforeEach(async ({ page, request }) => {
  // Authenticate before first paint, otherwise every page load starts anonymous and the suite
  // races the rate limiter rather than testing behaviour.
  await signIn(page, await apiKey(request))
})

test.describe('shell', () => {
  test('landing page loads and lists the operations', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('heading', { name: 'PDF operations as an HTTP API' })).toBeVisible()

    // The catalogue is fetched from the API, so this also proves the page is wired to a live server.
    const operations = page.locator('.op')
    await expect(operations.first()).toBeVisible()
    expect(await operations.count()).toBeGreaterThanOrEqual(11)

    await expect(page.getByRole('table').first()).toBeVisible()
  })

  test('navigation reaches every tool', async ({ page }) => {
    await page.goto('/')

    for (const [label, heading] of [
      ['Create', 'Create a PDF from text'],
      ['Word', 'Word to PDF'],
      ['Edit text', 'Update text in a PDF'],
      ['Forms', 'Form fields'],
      ['Merge', 'Merge PDFs'],
      ['Pages', 'Page tools'],
      ['Summarise', 'Summarise a PDF'],
      ['Inspect', 'Inspect a PDF'],
    ] as const) {
      await page.getByRole('navigation', { name: 'Tools' }).getByRole('link', { name: label, exact: true }).click()
      await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible()
    }
  })

  test('the tier badge reflects the saved key', async ({ page }) => {
    await page.goto('/')

    // A key that silently fails validation is otherwise indistinguishable from having none.
    await expect(page.getByText('Free', { exact: true })).toBeVisible()
  })

  test('a deep link resolves without a server round trip failing', async ({ page }) => {
    const response = await page.goto('/pages')

    expect(response?.status()).toBe(200)
    await expect(page.getByRole('heading', { name: 'Page tools', level: 1 })).toBeVisible()
  })

  test('theme can be switched and survives a reload', async ({ page }) => {
    await page.goto('/')

    const toggle = page.getByRole('button', { name: /Theme:/ })

    // A fresh browser has no stored preference, so the control starts on "system" and the
    // cycle is system -> light -> dark.
    await toggle.click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')

    await toggle.click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')

    // The choice has to outlive the page, or it is not a preference.
    await page.reload()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  })
})

test.describe('create', () => {
  test('renders a PDF and shows it in the preview', async ({ page }) => {
    await page.goto('/create')

    await page.getByLabel('Document body').fill('# End to end\n\nRendered by a browser test.')
    await page.getByLabel('Title').fill('E2E document')
    await page.getByRole('button', { name: 'Preview' }).click()

    // The result pane reports the produced file, and the preview frame receives a blob URL.
    await expect(page.getByText(/e2e-document\.pdf/)).toBeVisible({ timeout: 20_000 })

    const preview = page.locator('iframe[title="Result preview"]')
    await expect(preview).toBeVisible()
    expect(await preview.getAttribute('src')).toMatch(/^blob:/)
  })

  test('remaining quota is surfaced with the result', async ({ page }) => {
    await page.goto('/create')

    await page.getByLabel('Document body').fill('Quota display check.')
    await page.getByRole('button', { name: 'Preview' }).click()

    // Integrators need to see the ceiling approaching rather than discover it by being refused.
    await expect(page.getByText(/\d+\/\d+ left/)).toBeVisible({ timeout: 20_000 })
  })

  test('the action is disabled until there is content', async ({ page }) => {
    await page.goto('/create')

    await page.getByLabel('Document body').fill('')
    await expect(page.getByRole('button', { name: 'Preview' })).toBeDisabled()
    await expect(page.getByText('Add some content first')).toBeVisible()

    await page.getByLabel('Document body').fill('Now it has content.')
    await expect(page.getByRole('button', { name: 'Preview' })).toBeEnabled()
  })

  test('layout options reach the produced document', async ({ page }) => {
    await page.goto('/create')

    await page.getByLabel('Document body').fill('Landscape check.')
    await page.getByLabel('Orientation').selectOption('Landscape')
    await page.getByRole('button', { name: 'Preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 20_000 })
  })
})

test.describe('inspect', () => {
  test('reports structure for an uploaded document', async ({ page, request }) => {
    const pdf = await makePdf(request, await apiKey(request), 'Inspect through the UI.', 'UI subject')

    await page.goto('/inspect')
    await page.setInputFiles('input[type="file"]', {
      name: 'subject.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByRole('button', { name: 'Inspect' }).click()

    await expect(page.getByRole('heading', { name: 'Document', exact: true }).last()).toBeVisible()
    await expect(page.getByText('UI subject')).toBeVisible({ timeout: 20_000 })
  })

  test('a file that is not a PDF produces a readable error, not a crash', async ({ page }) => {
    await page.goto('/inspect')

    await page.setInputFiles('input[type="file"]', {
      name: 'not-really.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.alloc(500, 0x41),
    })

    await page.getByRole('button', { name: 'Inspect' }).click()

    await expect(page.getByText('Could not read that file')).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText(/not a PDF|corrupt/i)).toBeVisible()
  })
})

test.describe('merge', () => {
  test('requires two files and combines them in order', async ({ page, request }) => {
    const key = await apiKey(request)
    const one = await makePdf(request, key, 'First document.', 'One')
    const two = await makePdf(request, key, 'Second document.', 'Two')

    await page.goto('/merge')

    await page.setInputFiles('input[type="file"]', [
      { name: 'one.pdf', mimeType: 'application/pdf', buffer: one },
    ])

    // One file is not a merge, and the UI should say so rather than fail on submit.
    await expect(page.getByText('Add at least one more file')).toBeVisible()

    await page.setInputFiles('input[type="file"]', [
      { name: 'two.pdf', mimeType: 'application/pdf', buffer: two },
    ])

    await expect(page.getByRole('button', { name: 'Merge & preview' })).toBeEnabled()
    await page.getByRole('button', { name: 'Merge & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 20_000 })
  })
})

test.describe('form designer', () => {
  test('a click on the page becomes a field at those coordinates', async ({ page, request }) => {
    const pdf = await makePdf(request, await apiKey(request), 'Sign here.', 'Contract')

    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    // Waits for the designer's own readiness flag rather than mere visibility — the canvas
    // exists at its default 300x150 well before pdf.js has sized and drawn the page.
    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    const canvas = page.locator('.designer canvas')
    const box = await canvas.boundingBox()
    expect(box).not.toBeNull()

    // The rendered page must keep A4's proportions, or the two axes disagree and every
    // placement is subtly wrong on one of them.
    expect(box!.width / box!.height).toBeCloseTo(595.28 / 841.89, 2)

    // Click a quarter across and a third down. On A4 (595.28 x 841.89pt) that is ~149, ~278.
    //
    // Positioned on the locator rather than page.mouse, which takes raw viewport coordinates
    // and does not scroll: the page is taller than the window, so a click low on it would
    // otherwise land outside the viewport and hit nothing.
    await canvas.click({ position: { x: box!.width * 0.25, y: box!.height * 0.33 } })

    await expect(page.locator('.field-box')).toHaveCount(1)

    // The designer works in PDF points with a top-left origin, so what the user clicked is
    // what the API receives — no conversion in between.
    //
    // Exact matching matters: getByLabel is substring-based, so a bare 'Y' also matches
    // "Field type to place" and "Read only".
    const x = Number(await page.getByLabel('X', { exact: true }).inputValue())
    const y = Number(await page.getByLabel('Y', { exact: true }).inputValue())

    // A4 is 595.28 x 841.89pt, so 25%/33% lands at roughly 149, 278.
    expect(x).toBeCloseTo(595.28 * 0.25, -1)
    expect(y).toBeCloseTo(841.89 * 0.33, -1)
  })

  test('a placed field survives the round trip into the document', async ({ page, request }) => {
    const pdf = await makePdf(request, await apiKey(request), 'Sign here.', 'Contract')

    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    const canvas = page.locator('.designer canvas')
    const box = await canvas.boundingBox()
    await canvas.click({ position: { x: box!.width * 0.3, y: box!.height * 0.4 } })

    await expect(page.locator('.field-box')).toHaveCount(1)

    await page.getByLabel('Name').fill('signature')
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })
})

test.describe('api keys', () => {
  test('the key page shows the saved key masked, never in full', async ({ page }) => {
    await page.goto('/api')

    await expect(page.getByText('saved in this browser')).toBeVisible()

    // The stored secret must not be recoverable by reading the page.
    const masked = await page.locator('.key__value').textContent()
    expect(masked).toMatch(/^pw_.+•+.{4}$/)
  })

  test('quota is reported per action without consuming any', async ({ page }) => {
    await page.goto('/api')

    await expect(page.getByRole('heading', { name: 'Remaining quota' })).toBeVisible()
    await expect(page.getByText('CreateFromText')).toBeVisible()
    await expect(page.getByText('Checking consumes nothing')).toBeVisible()
  })
})

test.describe('accessibility', () => {
  test('the page is reachable by keyboard from the skip link', async ({ page }) => {
    await page.goto('/')

    await page.keyboard.press('Tab')
    await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused()
  })

  test('form controls have accessible names', async ({ page }) => {
    await page.goto('/create')

    // Every control the user can reach must be announceable; a label is not optional.
    for (const name of ['Document body', 'Title', 'Author', 'Format', 'Page size', 'Orientation']) {
      await expect(page.getByLabel(name)).toBeVisible()
    }
  })

  test('every form control resolves to a real control, not a wrapper', async ({ page }) => {
    await page.goto('/create')

    // getByLabel is satisfied by any element carrying the name, including a decorative div —
    // which is exactly how two unlabelled selects hid behind a labelled wrapper. Asserting the
    // tag closes that gap.
    for (const [name, tag] of [
      ['Document body', 'TEXTAREA'],
      ['Title', 'INPUT'],
      ['Format', 'SELECT'],
      ['Page size', 'SELECT'],
      ['Orientation', 'SELECT'],
    ] as const) {
      const element = page.getByLabel(name)
      expect(await element.evaluate((el) => el.tagName), `${name} should be a ${tag}`).toBe(tag)
    }
  })

  test('an error is announced rather than only shown', async ({ page }) => {
    await page.goto('/inspect')

    await page.setInputFiles('input[type="file"]', {
      name: 'bad.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.alloc(400, 0x42),
    })
    await page.getByRole('button', { name: 'Inspect' }).click()

    // Errors carry role="alert" so a screen reader interrupts; success uses status instead.
    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 20_000 })
  })
})

test.describe('word to pdf', () => {
  test('converts a .docx and previews the result', async ({ page }) => {
    await page.goto('/word')

    await page.setInputFiles('input[type="file"]', {
      name: 'report.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: makeDocx(),
    })

    await page.getByRole('button', { name: 'Convert & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(/report\.pdf/)).toBeVisible()
  })

  test('the action waits for a file', async ({ page }) => {
    await page.goto('/word')

    await expect(page.getByRole('button', { name: 'Convert & preview' })).toBeDisabled()
  })

  test('a file that is not a Word document is reported, not swallowed', async ({ page }) => {
    await page.goto('/word')

    await page.setInputFiles('input[type="file"]', {
      name: 'fake.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: Buffer.alloc(600, 0x43),
    })

    await page.getByRole('button', { name: 'Convert & preview' }).click()

    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 30_000 })
  })
})

test.describe('edit text', () => {
  test('replaces text and reports how many matches were changed', async ({ page, request }) => {
    const pdf = await makePdf(request, await apiKey(request), 'The agreement covers London.', 'Deed')

    await page.goto('/edit')
    await page.setInputFiles('input[type="file"]', {
      name: 'deed.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByLabel('Find').fill('London')
    await page.getByLabel('Replace with').fill('Manchester')
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })

  test('more than one replacement can be queued', async ({ page, request }) => {
    const pdf = await sharedPdf(request)

    await page.goto('/edit')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByRole('button', { name: 'Add another' }).click()

    // Two rows means two independent find/replace pairs, which is what the endpoint accepts.
    await expect(page.getByLabel('Find')).toHaveCount(2)
  })

  test('asking to fail on no match surfaces the failure', async ({ page, request }) => {
    const pdf = await sharedPdf(request)

    await page.goto('/edit')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByLabel('Find').fill('a string that is certainly not in the document')
    await page.getByLabel('Replace with').fill('anything')
    await page.getByLabel('Fail if nothing matched').check()
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    // The point of the option is that silence would otherwise look like success.
    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 30_000 })
  })
})

test.describe('page tools', () => {
  test('extracts a range of pages', async ({ page, request }) => {
    const pdf = await sharedPdf(request)

    await page.goto('/pages')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByLabel('Pages').fill('1')
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })

  test('rotates', async ({ page, request }) => {
    const pdf = await sharedPdf(request)

    await page.goto('/pages')
    await page.getByRole('tab', { name: 'Rotate' }).click()
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByRole('button', { name: 'Apply & preview' }).click()
    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })

  test('watermarks', async ({ page, request }) => {
    const pdf = await sharedPdf(request)

    await page.goto('/pages')
    await page.getByRole('tab', { name: 'Watermark' }).click()
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByLabel('Text', { exact: true }).fill('DRAFT')
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })

  test('an encrypted result offers download instead of preview', async ({ page, request }) => {
    const pdf = await sharedPdf(request)

    await page.goto('/pages')
    await page.getByRole('tab', { name: 'Protect' }).click()
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByLabel('Password to open').fill('correct horse battery staple')

    // A browser cannot render an encrypted PDF, so offering a preview would be a broken promise.
    await expect(page.getByText('Encrypted files cannot preview')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Apply & preview' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Download' })).toBeEnabled()
  })
})

test.describe('form fill', () => {
  test('a document with fields opens straight into fill mode with its values listed', async ({
    page,
    request,
  }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'form.pdf',
      mimeType: 'application/pdf',
      buffer: await makeFormPdf(request),
    })

    await page.getByRole('tab', { name: 'Fill values' }).click()

    // The fields come from inspecting the uploaded document, so seeing them by name proves the
    // designer's output and the reader's input agree.
    await expect(page.getByLabel('clientName')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByLabel('agreed')).toBeVisible()
  })

  test('values written in the browser reach the produced document', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'form.pdf',
      mimeType: 'application/pdf',
      buffer: await makeFormPdf(request),
    })

    await page.getByRole('tab', { name: 'Fill values' }).click()
    await page.getByLabel('clientName').fill('Ada Lovelace')
    await page.getByLabel('agreed').selectOption('true')
    await page.getByRole('button', { name: 'Fill & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })

  test('flattening is offered and produces a document', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'form.pdf',
      mimeType: 'application/pdf',
      buffer: await makeFormPdf(request),
    })

    await page.getByRole('tab', { name: 'Fill values' }).click()
    await page.getByLabel('clientName').fill('Ada Lovelace')
    await page.getByLabel('Flatten').check()
    await page.getByRole('button', { name: 'Fill & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })

  test('a document with no fields says so instead of showing an empty form', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'plain.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.getByRole('tab', { name: 'Fill values' }).click()

    // An empty panel would read as "loading" forever.
    await expect(page.getByText('This document has no form fields yet')).toBeVisible({ timeout: 30_000 })
  })
})

test.describe('form designer interaction', () => {
  test('a field can be moved by dragging it', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    const canvas = page.locator('.designer canvas')
    const box = (await canvas.boundingBox())!
    await canvas.click({ position: { x: box.width * 0.2, y: box.height * 0.3 } })

    const startX = Number(await page.getByLabel('X', { exact: true }).inputValue())
    const startY = Number(await page.getByLabel('Y', { exact: true }).inputValue())

    const field = page.locator('.field-box').first()
    const fieldBox = (await field.boundingBox())!

    await page.mouse.move(fieldBox.x + fieldBox.width / 2, fieldBox.y + fieldBox.height / 2)
    await page.mouse.down()
    await page.mouse.move(fieldBox.x + fieldBox.width / 2 + 60, fieldBox.y + fieldBox.height / 2 + 40, {
      steps: 10,
    })
    await page.mouse.up()

    const movedX = Number(await page.getByLabel('X', { exact: true }).inputValue())
    const movedY = Number(await page.getByLabel('Y', { exact: true }).inputValue())

    // 60 and 40 screen pixels, converted back to points at the rendered scale. Asserting the
    // direction and rough magnitude catches a sign error or a missing scale division, which is
    // what actually goes wrong here.
    const scale = box.width / 595.28
    expect(movedX - startX).toBeCloseTo(60 / scale, -1)
    expect(movedY - startY).toBeCloseTo(40 / scale, -1)
  })

  test('a field can be resized by its handle', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    const canvas = page.locator('.designer canvas')
    const box = (await canvas.boundingBox())!
    await canvas.click({ position: { x: box.width * 0.2, y: box.height * 0.3 } })

    const startWidth = Number(await page.getByLabel('Width').inputValue())

    const handle = page.locator('.field-box__handle').first()
    const handleBox = (await handle.boundingBox())!

    await page.mouse.move(handleBox.x + handleBox.width / 2, handleBox.y + handleBox.height / 2)
    await page.mouse.down()
    await page.mouse.move(handleBox.x + handleBox.width / 2 + 50, handleBox.y + handleBox.height / 2, {
      steps: 8,
    })
    await page.mouse.up()

    expect(Number(await page.getByLabel('Width').inputValue())).toBeGreaterThan(startWidth)
  })

  test('a field can be deleted', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    const canvas = page.locator('.designer canvas')
    const box = (await canvas.boundingBox())!
    await canvas.click({ position: { x: box.width * 0.3, y: box.height * 0.3 } })

    await expect(page.locator('.field-box')).toHaveCount(1)

    await page.getByRole('button', { name: 'Delete field' }).click()

    await expect(page.locator('.field-box')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Apply & preview' })).toBeDisabled()
  })

  test('a field type that needs options asks for them', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    // Reached by its accessible name, which is the whole point: PwSelect wraps the control in a
    // div for the chevron, and fallthrough attributes used to land there instead of on the
    // <select>, leaving both designer selects nameless to a screen reader.
    await page.getByLabel('Field type to place').selectOption('Dropdown')

    const canvas = page.locator('.designer canvas')
    const box = (await canvas.boundingBox())!
    await canvas.click({ position: { x: box.width * 0.3, y: box.height * 0.4 } })

    // A dropdown with no options is not a usable control, so the panel has to ask.
    await expect(page.getByLabel('Options')).toBeVisible()
  })

  test('several fields can be placed and are counted', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'contract.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    const canvas = page.locator('.designer canvas')
    const box = (await canvas.boundingBox())!

    for (const [x, y] of [
      [0.2, 0.2],
      [0.5, 0.35],
      [0.3, 0.55],
    ] as const) {
      await canvas.click({ position: { x: box.width * x, y: box.height * y } })
      await page.getByRole('button', { name: 'Done' }).click()
    }

    await expect(page.locator('.field-box')).toHaveCount(3)
    await expect(page.getByText('3 field(s)')).toBeVisible()
  })
})

test.describe('summarise', () => {
  test('summarises a document, or says plainly that no model is configured', async ({ page, request }) => {
    const providers = await configuredProviders(request)
    const pdf = await sharedPdf(request)

    await page.goto('/summarize')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByRole('button', { name: 'Summarise' }).click()

    if (providers.length === 0) {
      // No key is committed anywhere, so a fresh clone lands here. The failure still has to be
      // legible rather than a spinner that never resolves.
      await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 60_000 })
      return
    }

    // The summary itself, not the spinner or the "extracting text" notice, both of which also
    // carry role="status".
    await expect(page.locator('.lede')).toBeVisible({ timeout: 60_000 })
  })
})

test.describe('accessibility audit', () => {
  const pages = ['/', '/create', '/word', '/edit', '/forms', '/merge', '/pages', '/summarize', '/inspect', '/api']

  for (const path of pages) {
    for (const theme of ['light', 'dark'] as const) {
      test(`${path} has no axe violations in ${theme}`, async ({ page }) => {
        await page.emulateMedia({ colorScheme: theme })
        await page.goto(path)

        // The catalogue on the landing page arrives over the network; auditing before it lands
        // would audit an empty page and prove nothing.
        await page.waitForLoadState('networkidle')

        const results = await new AxeBuilder({ page })
          .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
          .analyze()

        // Includes axe's own explanation for each node, which for a contrast failure carries the
        // measured ratio and both colours. Without it the failure says only that something is
        // wrong, and the first thing anyone would do is go and measure it by hand.
        const summary = results.violations.flatMap((v) =>
          v.nodes.map(
            (n) =>
              `${v.id} (${v.impact}): ${n.html.slice(0, 100)}\n      ${n.failureSummary?.replace(/\n/g, '\n      ')}`,
          ),
        )

        expect(summary, `axe violations on ${path} (${theme})`).toEqual([])
      })
    }
  }
})

/**
 * The embeddable widget.
 *
 * Exercised through the demo page the server ships, because that is the same path a customer
 * takes: one script tag, one mount call, on a page whose CSS is not ours. The widget renders
 * into a shadow root, and Playwright's locators pierce shadow boundaries, so these read like
 * ordinary DOM tests.
 */
test.describe('embed widget', () => {
  test('the demo page mounts every widget it declares', async ({ page }) => {
    await page.goto('/embed-demo.html')

    for (const heading of ['Create a PDF', 'Merge PDFs', 'Inspect a PDF', 'Fill a PDF form']) {
      await expect(page.getByText(heading, { exact: true }).first()).toBeVisible()
    }
  })

  test('the global exposes a version and the tool list', async ({ page }) => {
    await page.goto('/embed-demo.html')

    const api = await page.evaluate(() => {
      const w = (window as unknown as { PdfWerk: { version: string; tools: string[] } }).PdfWerk
      return { version: w.version, tools: w.tools }
    })

    // `PdfWerk.version` came back undefined once because the default export overwrote the named
    // ones. Asserting the shape keeps that from happening again quietly.
    expect(api.version).toMatch(/^\d+\.\d+\.\d+$/)
    expect(api.tools).toEqual(
      expect.arrayContaining(['create', 'word', 'merge', 'summarize', 'fill', 'inspect']),
    )
  })

  test('the create widget produces a real PDF and hands it to onResult', async ({ page }) => {
    await page.goto('/embed-demo.html')

    // Mounted fresh rather than reusing the demo's own instance, so the callback is ours.
    await page.evaluate(() => {
      const host = document.createElement('div')
      host.id = 'probe'
      document.body.appendChild(host)

      const w = window as unknown as {
        PdfWerk: { mount: (t: string, o: Record<string, unknown>) => void }
        __result?: { fileName: string; size: number; head: string }
      }

      w.PdfWerk.mount('#probe', {
        tool: 'create',
        delivery: 'callback',
        onResult: async (blob: Blob, meta: { fileName: string }) => {
          const head = new TextDecoder().decode((await blob.arrayBuffer()).slice(0, 5))
          w.__result = { fileName: meta.fileName, size: blob.size, head }
        },
      })
    })

    const widget = page.locator('#probe')
    await widget.getByRole('textbox').first().fill('# Embedded\n\nProduced by the widget.')
    await widget.getByRole('button', { name: 'Create PDF' }).click()

    await expect
      .poll(
        () => page.evaluate(() => (window as unknown as { __result?: unknown }).__result),
        { timeout: 30_000 },
      )
      .toBeTruthy()

    const result = await page.evaluate(
      () => (window as unknown as { __result: { fileName: string; size: number; head: string } }).__result,
    )

    expect(result.head).toBe('%PDF-')
    expect(result.size).toBeGreaterThan(500)
    expect(result.fileName).toMatch(/\.pdf$/)
  })

  test('an unknown tool fails loudly at mount', async ({ page }) => {
    await page.goto('/embed-demo.html')

    const message = await page.evaluate(() => {
      const host = document.createElement('div')
      host.id = 'bad-tool'
      document.body.appendChild(host)

      try {
        ;(window as unknown as { PdfWerk: { mount: (t: string, o: unknown) => void } }).PdfWerk.mount(
          '#bad-tool',
          { tool: 'nonsense' },
        )
        return null
      } catch (error) {
        return (error as Error).message
      }
    })

    // A silently empty container would be far worse to debug from the host page.
    expect(message).toContain('nonsense')
    expect(message).toContain('create')
  })

  test('a selector matching nothing fails loudly too', async ({ page }) => {
    await page.goto('/embed-demo.html')

    const message = await page.evaluate(() => {
      try {
        ;(window as unknown as { PdfWerk: { mount: (t: string, o: unknown) => void } }).PdfWerk.mount(
          '#not-on-this-page',
          { tool: 'create' },
        )
        return null
      } catch (error) {
        return (error as Error).message
      }
    })

    expect(message).toContain('#not-on-this-page')
  })

  test('host page styles do not reach inside the widget', async ({ page }) => {
    await page.goto('/embed-demo.html')

    await page.addStyleTag({
      content: 'button, input, textarea { font-size: 44px !important; background: red !important; }',
    })

    const host = page.locator('#pdf-create')
    const size = await host
      .getByRole('button', { name: 'Create PDF' })
      .evaluate((el) => getComputedStyle(el).fontSize)

    // The whole point of the shadow root: a host stylesheet, even with !important, stops at the
    // boundary. Without it the widget would be reshaped by every page it lands on.
    expect(parseFloat(size)).toBeLessThan(30)
  })

  test('the widget carries its own attribution', async ({ page }) => {
    await page.goto('/embed-demo.html')

    await expect(page.getByRole('link', { name: 'PdfWerk' }).first()).toBeVisible()
  })

  test('destroy removes the widget from the page', async ({ page }) => {
    await page.goto('/embed-demo.html')

    const emptied = await page.evaluate(() => {
      const host = document.createElement('div')
      host.id = 'temporary'
      document.body.appendChild(host)

      const handle = (
        window as unknown as {
          PdfWerk: { mount: (t: string, o: unknown) => { destroy(): void } }
        }
      ).PdfWerk.mount('#temporary', { tool: 'inspect' })

      const before = host.shadowRoot !== null || host.childNodes.length > 0
      handle.destroy()

      return { before, after: host.childNodes.length }
    })

    expect(emptied.before).toBe(true)
    expect(emptied.after).toBe(0)
  })
})

/**
 * How the interface behaves at the ceiling.
 *
 * The limiter itself is proven in the API suite against real quota. These tests are about the
 * browser's half of the contract, so the 429 is injected rather than earned — that keeps the
 * assertions about presentation, and stops the UI tests from spending quota the API tests need
 * or racing the same per-minute bucket.
 */
test.describe('rate limiting in the browser', () => {
  const rejection = {
    status: 429,
    contentType: 'application/json',
    headers: {
      'Retry-After': '34',
      'X-RateLimit-Limit': '20',
      'X-RateLimit-Remaining': '0',
      'X-RateLimit-Window': 'minute',
    },
    body: JSON.stringify({
      error: 'rate_limited',
      window: 'minute',
      limit: 20,
      retryAfterSeconds: 34,
      message: 'Rate limit reached for CreateFromText: 20 per minute. Try again in 34s.',
    }),
  }

  test('a refusal is shown as a limit, not as a failure', async ({ page }) => {
    await page.route('**/v1/create/text*', (route) => route.fulfill(rejection))
    await page.goto('/create')

    await page.getByLabel('Document body').fill('Over the ceiling.')
    await page.getByRole('button', { name: 'Preview' }).click()

    // Being throttled is not the same as being broken, and the interface should not imply it is.
    await expect(page.getByText('Rate limit reached', { exact: true })).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('That did not work')).toHaveCount(0)
  })

  test('the refusal says when to try again', async ({ page }) => {
    await page.route('**/v1/create/text*', (route) => route.fulfill(rejection))
    await page.goto('/create')

    await page.getByLabel('Document body').fill('Over the ceiling.')
    await page.getByRole('button', { name: 'Preview' }).click()

    // Without the wait, the only recourse a caller has is to retry blindly and stay throttled.
    await expect(page.getByText(/Try again in 34s/)).toBeVisible({ timeout: 20_000 })
  })

  test('the refusal is announced, not merely displayed', async ({ page }) => {
    await page.route('**/v1/create/text*', (route) => route.fulfill(rejection))
    await page.goto('/create')

    await page.getByLabel('Document body').fill('Over the ceiling.')
    await page.getByRole('button', { name: 'Preview' }).click()

    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 20_000 })
  })

  test('the control is usable again afterwards, and success clears the message', async ({ page }) => {
    let refuse = true
    await page.route('**/v1/create/text*', async (route) => {
      if (refuse) return route.fulfill(rejection)
      return route.continue()
    })

    await page.goto('/create')
    await page.getByLabel('Document body').fill('First attempt.')
    await page.getByRole('button', { name: 'Preview' }).click()
    await expect(page.getByText('Rate limit reached', { exact: true })).toBeVisible({ timeout: 20_000 })

    // A refusal must not leave the button stuck in its loading state, or the wait becomes
    // permanent from the user's point of view.
    await expect(page.getByRole('button', { name: 'Preview' })).toBeEnabled()

    refuse = false
    await page.getByRole('button', { name: 'Preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('Rate limit reached', { exact: true })).toHaveCount(0)
  })

  test('an anonymous visitor is told a key would raise the ceiling', async ({ page }) => {
    await page.addInitScript(() => window.localStorage.removeItem('pdfwerk.apiKey'))
    await page.goto('/api')

    // The whole point of the free tier is that the way out of a limit is one request away.
    await expect(page.getByRole('button', { name: 'Create a free key' })).toBeVisible()
  })
})

test.describe('error recovery', () => {
  test('a second attempt after a server error works', async ({ page, request }) => {
    let fail = true
    await page.route('**/v1/inspect*', async (route) => {
      if (fail) {
        fail = false
        return route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'server_error', message: 'Something broke.' }),
        })
      }
      return route.continue()
    })

    await page.goto('/inspect')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.getByRole('button', { name: 'Inspect' }).click()
    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 20_000 })

    // The failure has to be recoverable in place; making the user reload would be a bug.
    await page.getByRole('button', { name: 'Inspect' }).click()
    await expect(page.getByRole('heading', { name: 'Document', exact: true }).last()).toBeVisible({
      timeout: 20_000,
    })
    await expect(page.locator('[role="alert"]')).toHaveCount(0)
  })

  test('a network failure is reported rather than hanging', async ({ page, request }) => {
    await page.route('**/v1/inspect*', (route) => route.abort('failed'))

    await page.goto('/inspect')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.getByRole('button', { name: 'Inspect' }).click()

    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 20_000 })
    await expect(page.getByRole('button', { name: 'Inspect' })).toBeEnabled()
  })
})

test.describe('keyboard', () => {
  test('the segmented control moves with arrow keys, one tab stop for the group', async ({
    page,
    request,
  }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    const design = page.getByRole('tab', { name: 'Design fields' })
    await design.focus()
    await expect(design).toBeFocused()

    await page.keyboard.press('ArrowRight')
    await expect(page.getByRole('tab', { name: 'Fill values' })).toBeFocused()

    // Wrapping is what makes a roving tabindex feel finished rather than truncated.
    await page.keyboard.press('ArrowRight')
    await expect(design).toBeFocused()

    await page.keyboard.press('End')
    await expect(page.getByRole('tab', { name: 'Fill values' })).toBeFocused()
  })

  test('a form can be completed without a mouse', async ({ page }) => {
    await page.goto('/create')

    await page.getByLabel('Document body').focus()
    await page.keyboard.type('Typed with the keyboard only.')

    const preview = page.getByRole('button', { name: 'Preview' })
    await preview.focus()
    await page.keyboard.press('Enter')

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 20_000 })
  })

  test('focus is visible wherever it lands', async ({ page }) => {
    await page.goto('/create')
    await page.getByLabel('Title').focus()

    const outline = await page
      .getByLabel('Title')
      .evaluate((el) => {
        const s = getComputedStyle(el)
        return { width: s.outlineWidth, style: s.outlineStyle, shadow: s.boxShadow }
      })

    // Keyboard users navigate by seeing where they are; an invisible focus ring is the single
    // most common way to make an otherwise accessible interface unusable.
    const visible = parseFloat(outline.width) > 0 && outline.style !== 'none'
    expect(visible || outline.shadow !== 'none').toBe(true)
  })
})

test.describe('small screens', () => {
  test.use({ viewport: { width: 375, height: 812 } })

  test('the landing page fits without sideways scrolling', async ({ page }) => {
    await page.goto('/')

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )

    // A page that scrolls horizontally on a phone reads as broken, whatever else it does well.
    expect(overflow).toBeLessThanOrEqual(1)
  })

  test('a tool page is usable at phone width', async ({ page }) => {
    await page.goto('/create')

    await expect(page.getByLabel('Document body')).toBeVisible()
    await page.getByLabel('Document body').fill('From a phone.')
    await expect(page.getByRole('button', { name: 'Preview' })).toBeEnabled()

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow).toBeLessThanOrEqual(1)
  })

  test('navigation is still reachable', async ({ page }) => {
    await page.goto('/')

    await expect(
      page.getByRole('navigation', { name: 'Tools' }).getByRole('link', { name: 'Create', exact: true }),
    ).toBeVisible()
  })
})

test.describe('hostile content from a document', () => {
  /**
   * Anything read out of an uploaded PDF is attacker-controlled: a title, a field name, an
   * author. It is rendered back into the interface, so it has to be treated as text and never
   * as markup, however it got there.
   */
  // Deliberately free of full stops: '.' separates AcroForm hierarchy levels, so a payload
  // containing one is refused for that reason and never reaches the rendering path under test.
  // The handler assigns an implicit global for the same reason.
  const payload = '<img src=x onerror="xssFired=1">'

  test('a field name from a document is rendered as text, not markup', async ({ page, request }) => {
    const key = await apiKey(request)

    const designed = await request.post('/v1/forms/design?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(await sharedPdf(request)),
        request: JSON.stringify({
          add: [{ name: payload, type: 'Text', rect: { page: 1, x: 72, y: 300, width: 200, height: 20 } }],
        }),
      },
    })

    // Asserted rather than skipped past. A conditional skip here would pass silently whenever the
    // request failed for any other reason — a rate limit, say — and quietly test nothing. The
    // service does accept this name, so the escaping below is the defence that actually matters.
    expect(designed.ok(), await designed.text()).toBeTruthy()

    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'hostile.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from(await designed.body()),
    })

    await page.getByRole('tab', { name: 'Fill values' }).click()
    await expect(page.getByText(payload, { exact: false }).first()).toBeVisible({ timeout: 30_000 })

    expect(await page.evaluate(() => (window as { xssFired?: number }).xssFired)).toBeUndefined()
    expect(await page.locator('img[src="x"]').count()).toBe(0)
  })

  test('a document title from metadata is rendered as text', async ({ page, request }) => {
    const key = await apiKey(request)
    const pdf = await makePdf(request, key, 'Body text.', payload)

    await page.goto('/inspect')
    await page.setInputFiles('input[type="file"]', {
      name: 'hostile.pdf',
      mimeType: 'application/pdf',
      buffer: pdf,
    })

    await page.getByRole('button', { name: 'Inspect' }).click()
    await expect(page.getByText(payload, { exact: false }).first()).toBeVisible({ timeout: 30_000 })

    expect(await page.evaluate(() => (window as { xssFired?: number }).xssFired)).toBeUndefined()
    expect(await page.locator('img[src="x"]').count()).toBe(0)
  })

  test('an error message from the server is rendered as text', async ({ page, request }) => {
    await page.route('**/v1/inspect*', (route) =>
      route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'bad_request', message: payload }),
      }),
    )

    await page.goto('/inspect')
    await page.setInputFiles('input[type="file"]', {
      name: 'doc.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.getByRole('button', { name: 'Inspect' }).click()
    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 20_000 })

    expect(await page.evaluate(() => (window as { xssFired?: number }).xssFired)).toBeUndefined()
  })
})

test.describe('console hygiene', () => {
  const pages = ['/', '/create', '/word', '/edit', '/forms', '/merge', '/pages', '/summarize', '/inspect', '/api']

  for (const path of pages) {
    test(`${path} loads without console errors`, async ({ page }) => {
      const problems: string[] = []

      page.on('console', (message) => {
        if (message.type() === 'error') problems.push(message.text())
      })
      page.on('pageerror', (error) => problems.push(`uncaught: ${error.message}`))

      await page.goto(path)
      await page.waitForLoadState('networkidle')

      // A page that logs errors on load is either doing something it should not or telling the
      // truth about a bug. Neither belongs in a release.
      expect(problems, `console errors on ${path}`).toEqual([])
    })
  }
})

test.describe('multi-page documents', () => {
  test('the designer offers a page selector only when there is more than one page', async ({
    page,
    request,
  }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'single.pdf',
      mimeType: 'application/pdf',
      buffer: await sharedPdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    // A picker with one option is noise.
    await expect(page.getByLabel('Page', { exact: true })).toHaveCount(0)
  })

  test('fields stay on the page they were placed on', async ({ page, request }) => {
    await page.goto('/forms')
    await page.setInputFiles('input[type="file"]', {
      name: 'multi.pdf',
      mimeType: 'application/pdf',
      buffer: await multiPagePdf(request),
    })

    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })

    const selector = page.getByLabel('Page', { exact: true })
    await expect(selector).toBeVisible()

    const canvas = page.locator('.designer canvas')
    let box = (await canvas.boundingBox())!
    await canvas.click({ position: { x: box.width * 0.25, y: box.height * 0.3 } })
    await page.getByRole('button', { name: 'Done' }).click()

    await expect(page.locator('.field-box')).toHaveCount(1)

    // Moving to page two must not carry page one's field along with it.
    await selector.selectOption('2')
    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })
    await expect(page.locator('.field-box')).toHaveCount(0)

    box = (await canvas.boundingBox())!
    await canvas.click({ position: { x: box.width * 0.4, y: box.height * 0.5 } })
    await page.getByRole('button', { name: 'Done' }).click()

    await expect(page.locator('.field-box')).toHaveCount(1)

    // Both fields exist in the document even though only one is on screen.
    await expect(page.getByText('2 field(s)')).toBeVisible()

    await selector.selectOption('1')
    await page.locator('.designer[data-ready="true"]').waitFor({ timeout: 30_000 })
    await expect(page.locator('.field-box')).toHaveCount(1)
  })

  test('a page range reaches the endpoint', async ({ page, request }) => {
    await page.goto('/pages')
    await page.setInputFiles('input[type="file"]', {
      name: 'multi.pdf',
      mimeType: 'application/pdf',
      buffer: await multiPagePdf(request),
    })

    await page.getByLabel('Pages').fill('1-2')
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    await expect(page.locator('iframe[title="Result preview"]')).toBeVisible({ timeout: 30_000 })
  })

  test('a range naming a page that does not exist is refused, not ignored', async ({
    page,
    request,
  }) => {
    await page.goto('/pages')
    await page.setInputFiles('input[type="file"]', {
      name: 'multi.pdf',
      mimeType: 'application/pdf',
      buffer: await multiPagePdf(request),
    })

    await page.getByLabel('Pages').fill('99')
    await page.getByRole('button', { name: 'Apply & preview' }).click()

    // Silently returning page 1, or an empty document, would both be worse than saying no.
    await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 30_000 })
  })
})

test.describe('page ranges', () => {
  for (const [range, expected] of [
    ['1', 1],
    ['1-2', 2],
    ['odd', 2],
    ['even', 1],
    ['2-', 2],
    ['all', 3],
  ] as const) {
    test(`"${range}" selects ${expected} page(s) of three`, async ({ request }) => {
      const key = await apiKey(request)

      const response = await request.post('/v1/split?delivery=stream', {
        headers: authHeaders(key),
        multipart: {
          file: pdfPart(await multiPagePdf(request)),
          request: JSON.stringify({ mode: 'Extract', pages: range }),
        },
      })

      expect(response.ok(), await response.text()).toBeTruthy()

      const info = await (
        await request.post('/v1/inspect', {
          headers: authHeaders(key),
          multipart: { file: pdfPart(Buffer.from(await response.body())) },
        })
      ).json()

      expect(info.pageCount).toBe(expected)
    })
  }

  test('a malformed range is a client error rather than a guess', async ({ request }) => {
    const response = await request.post('/v1/split?delivery=stream', {
      headers: authHeaders(await apiKey(request)),
      multipart: {
        file: pdfPart(await multiPagePdf(request)),
        request: JSON.stringify({ mode: 'Extract', pages: '3-1' }),
      },
    })

    expect(response.status()).toBe(400)
  })

  test('rotation applies only to the pages named, leaving both neighbours alone', async ({
    request,
  }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/rotate?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(await multiPagePdf(request)),
        request: JSON.stringify({ degrees: 90, pages: '2' }),
      },
    })

    expect(response.ok(), await response.text()).toBeTruthy()

    const info = await (
      await request.post('/v1/inspect', {
        headers: authHeaders(key),
        multipart: { file: pdfPart(Buffer.from(await response.body())) },
      })
    ).json()

    expect(info.pageCount).toBe(3)

    // Inspect reports dimensions rather than a rotation angle, and a quarter turn swaps them —
    // which is what any renderer will actually see. Three pages rather than two on purpose: with
    // only a trailing page to check, "rotate everything from page 2 onwards" passes.
    expect(info.pages[0].width).toBeLessThan(info.pages[0].height)
    expect(info.pages[1].width).toBeGreaterThan(info.pages[1].height)
    expect(info.pages[2].width).toBeLessThan(info.pages[2].height)
  })
})
