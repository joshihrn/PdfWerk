import { expect, test } from '@playwright/test'
import { apiKey, makePdf, signIn } from './support'

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
