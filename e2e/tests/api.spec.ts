import { expect, test, type APIRequestContext } from '@playwright/test'
import {
  apiKey,
  authHeaders,
  configuredProviders,
  isPdf,
  makeDocx,
  makePdf,
  pdfPart,
  sharedPdf,
} from './support'

/**
 * API-level coverage.
 *
 * These exercise the HTTP contract rather than the engine — the unit suite already covers PDF
 * internals. What matters here is what a real integrator sees: status codes, headers, delivery
 * modes, and whether the quota is actually enforced over the wire.
 */

test.describe('discovery', () => {
  test('health reports the service', async ({ request }) => {
    const response = await request.get('/health')

    expect(response.ok()).toBeTruthy()
    expect(await response.json()).toMatchObject({ status: 'ok', service: 'pdfwerk' })
  })

  test('the action catalogue lists every operation with its endpoint', async ({ request }) => {
    const response = await request.get('/v1/actions')
    expect(response.ok()).toBeTruthy()

    const actions = await response.json()
    expect(actions.length).toBeGreaterThanOrEqual(11)

    // The UI builds its navigation from this, so the shape is a contract.
    for (const action of actions) {
      expect(action).toHaveProperty('action')
      expect(action).toHaveProperty('title')
      expect(action.endpoint).toMatch(/^\/v1\//)
    }

    const names = actions.map((a: { action: string }) => a.action)
    expect(names).toEqual(expect.arrayContaining(['CreateFromText', 'Merge', 'Split', 'Protect']))
  })

  test('OpenAPI document is served and describes the operations', async ({ request }) => {
    const response = await request.get('/openapi/v1.json')
    expect(response.ok()).toBeTruthy()

    const document = await response.json()
    expect(document.info.title).toBe('PdfWerk API')
    expect(Object.keys(document.paths)).toEqual(
      expect.arrayContaining(['/v1/create/text', '/v1/merge', '/v1/split']),
    )
  })

  test('providers report whether they can actually be used', async ({ request }) => {
    const providers = await (await request.get('/v1/providers')).json()

    expect(providers.map((p: { key: string }) => p.key)).toEqual(
      expect.arrayContaining(['gemini', 'groq', 'ollama']),
    )

    for (const provider of providers) {
      expect(typeof provider.configured).toBe('boolean')
    }
  })
})

test.describe('creating documents', () => {
  test('renders Markdown and names the file from the title', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/create/text?delivery=download', {
      headers: authHeaders(key),
      data: {
        content: '# Invoice 2026-004\n\nBilled to **Acme**.\n\n| Item | Amount |\n| --- | ---: |\n| Setup | 1200 |',
        title: 'Invoice 2026-004',
        format: 'Markdown',
      },
    })

    expect(response.status()).toBe(200)
    expect(response.headers()['content-type']).toContain('application/pdf')

    // The server derives a safe download name from the title rather than echoing input.
    expect(response.headers()['content-disposition']).toContain('invoice-2026-004.pdf')

    const body = Buffer.from(await response.body())
    expect(isPdf(body)).toBeTruthy()
    expect(body.length).toBeGreaterThan(1000)
  })

  test('delivery mode changes how the same document is returned', async ({ request }) => {
    const key = await apiKey(request)
    const payload = { content: 'Delivery mode test.', format: 'Plain' }

    const download = await request.post('/v1/create/text?delivery=download', {
      headers: authHeaders(key),
      data: payload,
    })
    const stream = await request.post('/v1/create/text?delivery=stream', {
      headers: authHeaders(key),
      data: payload,
    })
    const json = await request.post('/v1/create/text?delivery=json', {
      headers: authHeaders(key),
      data: payload,
    })

    // Download tells the browser to save it; stream deliberately does not, so it renders inline.
    expect(download.headers()['content-disposition']).toContain('attachment')
    expect(stream.headers()['content-disposition'] ?? '').not.toContain('attachment')

    const envelope = await json.json()
    expect(envelope).toMatchObject({ contentType: 'application/pdf' })
    expect(isPdf(Buffer.from(envelope.base64, 'base64'))).toBeTruthy()
  })

  test('empty content is rejected as a client error', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/create/text', {
      headers: authHeaders(key),
      data: { content: '   ', format: 'Plain' },
    })

    expect(response.status()).toBe(400)
    expect((await response.json()).error).toBe('bad_request')
  })
})

test.describe('document operations', () => {
  test('inspect reports structure without modifying anything', async ({ request }) => {
    const key = await apiKey(request)
    const pdf = await makePdf(request, key, 'Inspect me.', 'Inspection subject')

    const response = await request.post('/v1/inspect', {
      headers: authHeaders(key),
      multipart: { file: pdfPart(pdf) },
    })

    expect(response.ok()).toBeTruthy()

    const info = await response.json()
    expect(info.pageCount).toBe(1)
    expect(info.isEncrypted).toBe(false)
    expect(info.pages[0].width).toBeCloseTo(595.28, 1)   // A4 portrait, in points
  })

  test('merge concatenates in the order supplied', async ({ request }) => {
    const key = await apiKey(request)
    const first = await makePdf(request, key, 'MARKERONE', 'One')
    const second = await makePdf(request, key, 'MARKERTWO', 'Two')

    const merged = await request.post('/v1/merge?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        'files[0]': pdfPart(first, 'one.pdf'),
        'files[1]': pdfPart(second, 'two.pdf'),
      },
    })

    expect(merged.ok()).toBeTruthy()

    const info = await (
      await request.post('/v1/inspect', {
        headers: authHeaders(key),
        multipart: { file: pdfPart(Buffer.from(await merged.body()), 'merged.pdf') },
      })
    ).json()

    expect(info.pageCount).toBe(2)
  })

  test('form fields round-trip through design, inspect and fill', async ({ request }) => {
    const key = await apiKey(request)
    const pdf = await makePdf(request, key, 'Please sign below.', 'Agreement')

    // Design: place a field at a known position.
    const designed = await request.post('/v1/forms/design?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(pdf),
        request: JSON.stringify({
          add: [
            {
              name: 'clientName',
              type: 'Text',
              rect: { page: 1, x: 72, y: 300, width: 240, height: 22 },
            },
          ],
        }),
      },
    })

    expect(designed.ok()).toBeTruthy()
    const withField = Buffer.from(await designed.body())

    // Inspect: the coordinates must come back exactly as sent, or the designer would drift.
    const info = await (
      await request.post('/v1/inspect', {
        headers: authHeaders(key),
        multipart: { file: pdfPart(withField) },
      })
    ).json()

    expect(info.fields).toHaveLength(1)
    expect(info.fields[0]).toMatchObject({ name: 'clientName', type: 'Text' })
    expect(info.fields[0].rect).toMatchObject({ page: 1, x: 72, y: 300, width: 240, height: 22 })

    // Fill: the value must read back.
    const filled = await request.post('/v1/forms/fill?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(withField),
        request: JSON.stringify({ values: { clientName: 'Ada Lovelace' } }),
      },
    })

    const afterFill = await (
      await request.post('/v1/inspect', {
        headers: authHeaders(key),
        multipart: { file: pdfPart(Buffer.from(await filled.body())) },
      })
    ).json()

    expect(afterFill.fields[0].value).toBe('Ada Lovelace')
  })

  test('split bursts a document into a zip of single pages', async ({ request }) => {
    const key = await apiKey(request)
    const one = await makePdf(request, key, 'Page one.', 'One')
    const two = await makePdf(request, key, 'Page two.', 'Two')

    const merged = await request.post('/v1/merge?delivery=stream', {
      headers: authHeaders(key),
      multipart: { 'files[0]': pdfPart(one, 'a.pdf'), 'files[1]': pdfPart(two, 'b.pdf') },
    })

    const split = await request.post('/v1/split', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(Buffer.from(await merged.body()), 'both.pdf'),
        request: JSON.stringify({ pages: 'all', mode: 'Burst' }),
      },
    })

    expect(split.ok()).toBeTruthy()
    expect(split.headers()['content-type']).toContain('application/zip')
    expect(split.headers()['x-pdfwerk-parts']).toBe('2')

    // A zip begins with the local file header signature "PK\x03\x04".
    const archive = Buffer.from(await split.body())
    expect(archive.subarray(0, 4)).toEqual(Buffer.from([0x50, 0x4b, 0x03, 0x04]))
  })

  test('rotate turns only the pages it was asked to', async ({ request }) => {
    const key = await apiKey(request)
    const one = await makePdf(request, key, 'One.', 'One')
    const two = await makePdf(request, key, 'Two.', 'Two')

    const merged = await request.post('/v1/merge?delivery=stream', {
      headers: authHeaders(key),
      multipart: { 'files[0]': pdfPart(one, 'a.pdf'), 'files[1]': pdfPart(two, 'b.pdf') },
    })

    const rotated = await request.post('/v1/rotate?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(Buffer.from(await merged.body()), 'both.pdf'),
        request: JSON.stringify({ pages: '2', degrees: 90 }),
      },
    })

    const info = await (
      await request.post('/v1/inspect', {
        headers: authHeaders(key),
        multipart: { file: pdfPart(Buffer.from(await rotated.body())) },
      })
    ).json()

    // A quarter turn swaps the reported dimensions, which is what any renderer will see.
    expect(info.pages[0].width).toBeLessThan(info.pages[0].height)
    expect(info.pages[1].width).toBeGreaterThan(info.pages[1].height)
  })

  test('word conversion reports which converter ran', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/create/word?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: {
          name: 'report.docx',
          mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          buffer: makeDocx(),
        },
      },
    })

    expect(response.ok()).toBeTruthy()
    expect(isPdf(Buffer.from(await response.body()))).toBeTruthy()

    // The two paths differ in fidelity, so the caller is always told which produced the file.
    expect(['libreoffice', 'openxml']).toContain(response.headers()['x-pdfwerk-converter'])
  })
})

test.describe('error handling', () => {
  test('a file that is not a PDF is a 422, not a server error', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/inspect', {
      headers: authHeaders(key),
      multipart: {
        file: { name: 'fake.pdf', mimeType: 'application/pdf', buffer: Buffer.alloc(400, 0x78) },
      },
    })

    // Malformed uploads are the most common hostile input a public endpoint receives; they
    // must never surface as a 500.
    expect(response.status()).toBe(422)
    expect((await response.json()).error).toBe('invalid_pdf')
  })

  test('a truncated PDF is rejected cleanly', async ({ request }) => {
    const key = await apiKey(request)
    const pdf = await sharedPdf(request)

    const response = await request.post('/v1/inspect', {
      headers: authHeaders(key),
      multipart: { file: pdfPart(pdf.subarray(0, Math.floor(pdf.length / 3))) },
    })

    expect(response.status()).toBeLessThan(500)
  })

  test('an unknown AI provider is a client error naming the valid ones', async ({ request }) => {
    const key = await apiKey(request)
    const pdf = await sharedPdf(request)

    const response = await request.post('/v1/summarize', {
      headers: authHeaders(key),
      multipart: { file: pdfPart(pdf), request: JSON.stringify({ provider: 'not-a-provider' }) },
    })

    expect(response.status()).toBe(400)
    expect((await response.json()).message).toContain('gemini')
  })
})

test.describe('API keys and quota', () => {
  test('a key is issued once and raises the tier', async ({ request }) => {
    const created = await request.post('/v1/keys', { data: { label: 'issued in a test' } })
    expect(created.ok()).toBeTruthy()

    const issued = await created.json()
    expect(issued.key).toMatch(/^pw_/)
    expect(issued.tier).toBe('Free')
    expect(issued.warning).toContain('cannot be shown again')

    const me = await (await request.get('/v1/keys/me', { headers: authHeaders(issued.key) })).json()
    expect(me).toMatchObject({ authenticated: true, tier: 'Free', label: 'issued in a test' })
  })

  test('an unknown key falls back to anonymous rather than failing the request', async ({ request }) => {
    const response = await request.get('/v1/keys/me', {
      headers: { 'X-Api-Key': 'pw_thisKeyWasNeverIssuedAnywhere000000000000' },
    })

    expect(response.ok()).toBeTruthy()

    const body = await response.json()
    expect(body.authenticated).toBe(false)
    expect(body.message).toContain('unknown')
  })

  test('a revoked key stops working immediately', async ({ request }) => {
    const issued = await (await request.post('/v1/keys', { data: { label: 'to be revoked' } })).json()

    const revoked = await request.delete('/v1/keys/me', { headers: authHeaders(issued.key) })
    expect(revoked.ok()).toBeTruthy()

    const after = await (await request.get('/v1/keys/me', { headers: authHeaders(issued.key) })).json()
    expect(after.authenticated).toBe(false)
  })

  test('quota headers are present on every response', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/create/text?delivery=stream', {
      headers: authHeaders(key),
      data: { content: 'Quota header check.', format: 'Plain' },
    })

    const headers = response.headers()
    expect(headers['x-ratelimit-limit']).toBeDefined()
    expect(headers['x-ratelimit-remaining']).toBeDefined()
    expect(Number(headers['x-ratelimit-remaining'])).toBeLessThan(Number(headers['x-ratelimit-limit']))
  })

  test('reading quota consumes none of it', async ({ request }) => {
    const key = await apiKey(request)

    const before = await (await request.get('/v1/quota', { headers: authHeaders(key) })).json()
    await request.get('/v1/quota', { headers: authHeaders(key) })
    const after = await (await request.get('/v1/quota', { headers: authHeaders(key) })).json()

    const remaining = (report: { quotas: { action: string; remaining: Record<string, string> }[] }) =>
      report.quotas.find((q) => q.action === 'CreateFromText')!.remaining.minute

    expect(remaining(after)).toBe(remaining(before))
  })

  /**
   * Runs last and without a key, deliberately.
   *
   * It exhausts the anonymous bucket on purpose, so anything sharing that bucket afterwards
   * would fail on quota rather than on its own behaviour.
   */
  test('the rate limiter actually refuses over-quota traffic', async ({ request }) => {
    const statuses: number[] = []

    for (let i = 0; i < 9; i++) {
      const response = await request.post('/v1/create/text?delivery=stream', {
        data: { content: `Burst ${i}.`, format: 'Plain' },
      })
      statuses.push(response.status())
    }

    const allowed = statuses.filter((s) => s === 200).length
    const limited = statuses.filter((s) => s === 429).length

    // Anonymous is 5/min for this action. Fewer may be allowed if an earlier test spent some,
    // which is fine — the assertion is that the ceiling exists and is enforced.
    expect(allowed).toBeLessThanOrEqual(5)
    expect(limited).toBeGreaterThan(0)

    const rejection = await request.post('/v1/create/text', {
      data: { content: 'One more.', format: 'Plain' },
    })

    expect(rejection.status()).toBe(429)

    const body = await rejection.json()
    expect(body.error).toBe('rate_limited')
    expect(body.window).toBe('minute')
    expect(body.retryAfterSeconds).toBeGreaterThan(0)
  })
})

test.describe('text replacement', () => {
  test('replaces text and reports how many instructions matched', async ({ request }) => {
    const key = await apiKey(request)
    const pdf = await makePdf(request, key, 'The agreement covers London and Bristol.', 'Deed')

    const response = await request.post('/v1/edit/text?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(pdf),
        request: JSON.stringify({
          replacements: [{ find: 'London', replace: 'Manchester', matchCase: true }],
          failOnNoMatch: true,
        }),
      },
    })

    expect(response.ok(), await response.text()).toBeTruthy()
    expect(isPdf(Buffer.from(await response.body()))).toBe(true)
  })

  test('the replacement is actually in the document, and the original is not', async ({ request }) => {
    // Reading the text back goes through summarisation, which calls a model.
    test.setTimeout(120_000)

    // Declared up front rather than degraded silently. Reading the text back needs a model, and
    // quietly dropping the assertions when none is configured would report green for a test that
    // checked nothing — which is worse than an honest skip.
    test.skip(
      (await configuredProviders(request)).length === 0,
      'text extraction needs a configured AI provider',
    )

    const key = await apiKey(request)
    // Long enough to be summarisable: reading the text back goes through summarisation, which
    // refuses documents too short to condense.
    const pdf = await makePdf(
      request,
      key,
      'This agreement covers London and the surrounding counties for the full term of the lease.',
      'Deed',
    )

    const edited = await request.post('/v1/edit/text?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(pdf),
        request: JSON.stringify({
          replacements: [{ find: 'London', replace: 'Manchester' }],
        }),
      },
    })

    expect(edited.ok()).toBeTruthy()

    // Read it back through the service rather than trusting the response. Covering old text with
    // a white box would pass a visual check while leaving it selectable and searchable, which is
    // a disclosure risk, so the assertion is that the original is genuinely gone.
    // The field is `includeExtractedText`. Naming it `includeText` gets a cheerful 200 with no
    // text in it, because unknown JSON properties are ignored — which is how this test spent its
    // first run asserting against undefined.
    const text = await (
      await request.post('/v1/summarize', {
        headers: authHeaders(key),
        // The suite-wide actionTimeout is 10s, which a model round trip will exceed. Raising the
        // test timeout alone does not help: the per-action limit fires first.
        timeout: 90_000,
        multipart: {
          file: pdfPart(Buffer.from(await edited.body())),
          request: JSON.stringify({ includeExtractedText: true, targetWords: 20 }),
        },
      })
    ).json().catch(() => null)

    expect(text?.extractedText, 'the service should have returned the extracted text').toBeTruthy()

    // Covering the old text with a white box would pass a visual check while leaving it
    // selectable, searchable and copyable — a disclosure risk, not a cosmetic one.
    expect(text.extractedText).toContain('Manchester')
    expect(text.extractedText).not.toContain('London')
  })

  test('a short but readable document is not blamed on scanning', async ({ request }) => {
    test.setTimeout(120_000)
    test.skip((await configuredProviders(request)).length === 0, 'needs a configured AI provider')

    const key = await apiKey(request)
    const pdf = await makePdf(request, key, 'Paid in full.', null as unknown as string)

    const response = await request.post('/v1/summarize', {
      headers: authHeaders(key),
      multipart: { file: pdfPart(pdf), request: JSON.stringify({ targetWords: 20 }) },
    })

    expect(response.status()).toBe(400)

    const body = await response.json()

    // Sending someone to OCR a document that is already perfectly readable costs far more time
    // than the operation itself. The refusal has to name the real reason.
    expect(body.message).not.toMatch(/OCR/i)
    expect(body.message).toMatch(/too little/i)
  })

  test('failOnNoMatch turns a silent no-op into an error', async ({ request }) => {
    const key = await apiKey(request)
    const pdf = await sharedPdf(request)

    const response = await request.post('/v1/edit/text?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(pdf),
        request: JSON.stringify({
          replacements: [{ find: 'nothing in this document says this', replace: 'x' }],
          failOnNoMatch: true,
        }),
      },
    })

    // 400 rather than 422 by convention: 422 is reserved for a document that could not be read,
    // and this one read fine — it simply does not contain the text. Returning it unchanged with
    // a 200 is the thing that would be wrong, because it would look like success.
    expect(response.status()).toBe(400)

    const body = await response.json()
    expect(body.error).toBe('bad_request')

    // Scanned pages are the usual cause and are invisible from the caller's side, so the message
    // has to say so rather than leaving them to guess at their own search term.
    expect(body.message).toMatch(/scanned|character mapping/i)
    expect(body.message).toContain('failOnNoMatch')
  })

  test('PDF syntax inside replacement text does not escape into the content stream', async ({
    request,
  }) => {
    const key = await apiKey(request)
    const pdf = await makePdf(request, key, 'Replace the TARGET please.', 'Injection')

    const response = await request.post('/v1/edit/text?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(pdf),
        request: JSON.stringify({
          replacements: [{ find: 'TARGET', replace: ') Tj 0 0 1 rg (pwned' }],
        }),
      },
    })

    expect(response.ok()).toBeTruthy()

    // The result still has to be a readable PDF; a broken one would mean the parentheses were
    // written straight through rather than escaped.
    const out = Buffer.from(await response.body())
    expect(isPdf(out)).toBe(true)

    const inspected = await request.post('/v1/inspect', {
      headers: authHeaders(key),
      multipart: { file: pdfPart(out) },
    })

    expect(inspected.ok()).toBeTruthy()
  })
})

test.describe('watermark and protect', () => {
  test('watermarking returns a document that still parses', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/watermark?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(await sharedPdf(request)),
        request: JSON.stringify({ text: 'DRAFT', opacity: 0.2, color: '#FF0000' }),
      },
    })

    expect(response.ok(), await response.text()).toBeTruthy()

    const out = Buffer.from(await response.body())
    expect(isPdf(out)).toBe(true)

    const info = await (
      await request.post('/v1/inspect', { headers: authHeaders(key), multipart: { file: pdfPart(out) } })
    ).json()

    expect(info.pageCount).toBeGreaterThan(0)
  })

  test('an opacity outside 0 to 1 is a client error', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/watermark?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(await sharedPdf(request)),
        request: JSON.stringify({ text: 'DRAFT', opacity: 5 }),
      },
    })

    expect(response.status()).toBe(400)
  })

  test('protecting produces a document that reports itself as encrypted', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/protect?delivery=stream', {
      headers: authHeaders(key),
      multipart: {
        file: pdfPart(await sharedPdf(request)),
        request: JSON.stringify({
          userPassword: 'correct horse battery staple',
          permissions: { allowPrinting: true, allowCopying: false },
        }),
      },
    })

    expect(response.ok(), await response.text()).toBeTruthy()

    const out = Buffer.from(await response.body())
    expect(isPdf(out)).toBe(true)

    // /Encrypt has to be detectable in the file itself. The library's own IsEncrypted property
    // describes pending write state and returned false for every document, which is why this
    // asserts against the bytes.
    expect(out.toString('latin1')).toMatch(/\/Encrypt\s+\d+\s+\d+\s+R/)
  })

  test('an encrypted document cannot then be inspected without the password', async ({ request }) => {
    const key = await apiKey(request)

    const protectedPdf = Buffer.from(
      await (
        await request.post('/v1/protect?delivery=stream', {
          headers: authHeaders(key),
          multipart: {
            file: pdfPart(await sharedPdf(request)),
            request: JSON.stringify({ userPassword: 'secret' }),
          },
        })
      ).body(),
    )

    const inspected = await request.post('/v1/inspect', {
      headers: authHeaders(key),
      multipart: { file: pdfPart(protectedPdf) },
    })

    // A 500 here would mean an unhandled library exception rather than a considered refusal.
    expect(inspected.status()).toBe(422)
  })
})

test.describe('cross-origin access', () => {
  test('a preflight from another origin is allowed', async ({ request }) => {
    const response = await request.fetch('/v1/create/text', {
      method: 'OPTIONS',
      headers: {
        Origin: 'https://someone-elses-site.example',
        'Access-Control-Request-Method': 'POST',
        'Access-Control-Request-Headers': 'content-type,x-api-key',
      },
    })

    expect(response.status()).toBeLessThan(400)
    expect(response.headers()['access-control-allow-origin']).toBeTruthy()
  })

  test('the headers an integrator needs are exposed to script', async ({ request }) => {
    // Asked of a metadata endpoint rather than a document one. The exposed list belongs to the
    // CORS policy and is identical on every response, and creating a document here would spend
    // quota from the same per-minute bucket the create tests need.
    const response = await request.get('/v1/actions', {
      headers: { Origin: 'https://someone-elses-site.example' },
    })

    expect(response.ok()).toBeTruthy()

    // Without these on the exposed list the browser hides them from fetch, and the embedded
    // widget cannot name its own download or show remaining quota — the two things it needs.
    const exposed = (response.headers()['access-control-expose-headers'] ?? '').toLowerCase()

    for (const header of ['content-disposition', 'x-ratelimit-remaining']) {
      expect(exposed, `${header} must be readable cross-origin`).toContain(header)
    }
  })
})

test.describe('response shape', () => {
  test('a hostile filename cannot escape the Content-Disposition header', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.post('/v1/create/text?delivery=download', {
      headers: authHeaders(key),
      data: {
        content: 'Naming test.',
        title: '../../etc/passwd"\r\nX-Injected: yes',
        format: 'Plain',
      },
    })

    expect(response.ok()).toBeTruthy()

    const disposition = response.headers()['content-disposition'] ?? ''

    expect(disposition).not.toContain('..')
    expect(response.headers()['x-injected']).toBeUndefined()
  })

  test('every document response names the action that produced it', async ({ request }) => {
    const response = await request.post('/v1/create/text?delivery=stream', {
      headers: authHeaders(await apiKey(request)),
      data: { content: 'Action header.', format: 'Plain' },
    })

    expect(response.headers()['x-pdfwerk-action']).toBeTruthy()
  })
})

/**
 * What a crawler receives.
 *
 * The app renders in the browser, so everything here is fetched as plain HTTP with no JavaScript
 * anywhere — which is exactly how Bing, and every social scraper, sees the site. If these pass,
 * a link to any page previews and indexes as itself rather than as the generic shell.
 */
test.describe('search engine metadata', () => {
  const ROUTES = ['/', '/create', '/word', '/edit', '/forms', '/merge', '/pages', '/summarize', '/inspect', '/api', '/contact', '/privacy', '/terms']

  test('every page carries its own title and description', async ({ request }) => {
    const seen = new Map<string, string>()

    for (const route of ROUTES) {
      const html = await (await request.get(route)).text()

      const title = html.match(/<title>([^<]+)<\/title>/)?.[1]
      const description = html.match(/<meta name="description" content="([^"]+)"/)?.[1]

      expect(title, `${route} should have a title`).toBeTruthy()
      expect(description, `${route} should have a description`).toBeTruthy()

      // A description over about 160 characters is truncated in the results page, so the tail of
      // it is wasted effort rather than a mistake — but under 70 usually means it says nothing.
      expect(description!.length, `${route} description length`).toBeGreaterThan(70)

      // Duplicate titles across pages are the single most common way a small site competes with
      // itself, and the reason the shell's one shared title was worth replacing.
      expect(seen.has(title!), `${route} duplicates the title of ${seen.get(title!)}`).toBe(false)
      seen.set(title!, route)
    }
  })

  test('every page declares itself canonical at the configured origin', async ({ request }) => {
    for (const route of ROUTES) {
      const html = await (await request.get(route)).text()
      const canonical = html.match(/<link rel="canonical" href="([^"]+)"/)?.[1]

      expect(canonical, `${route} should be canonical`).toBeTruthy()
      expect(canonical).toMatch(/^https?:\/\//)
      expect(canonical!.endsWith(route === '/' ? '/' : route)).toBe(true)
    }
  })

  test('a shared link previews as the page, not as the site', async ({ request }) => {
    const html = await (await request.get('/merge')).text()

    for (const tag of ['og:title', 'og:description', 'og:url', 'og:image', 'og:site_name']) {
      expect(html, `missing ${tag}`).toContain(`property="${tag}"`)
    }

    for (const tag of ['twitter:card', 'twitter:title', 'twitter:description', 'twitter:image']) {
      expect(html, `missing ${tag}`).toContain(`name="${tag}"`)
    }

    // The Open Graph title has to be the page's, or the preview says "PdfWerk" for all ten.
    const ogTitle = html.match(/property="og:title" content="([^"]+)"/)?.[1]
    expect(ogTitle).toContain('Merge')
  })

  test('the preview image exists and is the size every scraper crops to', async ({ request }) => {
    const response = await request.get('/og.png')

    expect(response.status()).toBe(200)
    expect(response.headers()['content-type']).toContain('image/png')

    // PNG header: an 8-byte signature, then IHDR carrying width and height as big-endian ints.
    const bytes = Buffer.from(await response.body())
    expect(bytes.subarray(1, 4).toString('ascii')).toBe('PNG')

    expect(bytes.readUInt32BE(16)).toBe(1200)
    expect(bytes.readUInt32BE(20)).toBe(630)
  })

  test('structured data is valid JSON and describes the page', async ({ request }) => {
    const html = await (await request.get('/forms')).text()
    const block = html.match(/<script type="application\/ld\+json">(.+?)<\/script>/s)?.[1]

    expect(block, 'no structured data').toBeTruthy()

    // Malformed JSON-LD is silently ignored by every consumer, so it fails without telling you.
    const data = JSON.parse(block!)

    expect(data['@context']).toBe('https://schema.org')
    expect(data['@type']).toBeTruthy()
    expect(data.url).toContain('/forms')
  })

  test('robots.txt points at the sitemap and keeps crawlers out of the API', async ({ request }) => {
    const response = await request.get('/robots.txt')

    expect(response.status()).toBe(200)
    expect(response.headers()['content-type']).toContain('text/plain')

    const body = await response.text()

    expect(body).toContain('User-agent: *')
    expect(body).toMatch(/Sitemap: https?:\/\/\S+\/sitemap\.xml/)

    // Crawling the operation endpoints would spend a caller's quota and index a stream of bytes.
    expect(body).toContain('Disallow: /v1/')
  })

  test('the sitemap is well formed and lists every page', async ({ request }) => {
    const response = await request.get('/sitemap.xml')

    expect(response.status()).toBe(200)
    expect(response.headers()['content-type']).toContain('xml')

    const body = await response.text()

    expect(body.startsWith('<?xml version="1.0" encoding="utf-8"?>')).toBe(true)
    expect(body).toContain('http://www.sitemaps.org/schemas/sitemap/0.9')

    // Generated from the same catalogue the pages use, so this also proves the two agree — a
    // sitemap maintained by hand drifts the first time a route is added.
    const locations = [...body.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1])
    expect(locations).toHaveLength(ROUTES.length)

    for (const route of ROUTES) {
      expect(locations.some((l) => l.endsWith(route === '/' ? '/' : route)), `${route} missing`).toBe(true)
    }
  })

  test('a page without content asks not to be indexed', async ({ request }) => {
    const response = await request.get('/this-route-does-not-exist')

    // The router will redirect a visitor home, but a crawler should not bank the URL first.
    expect(response.headers()['x-robots-tag']).toBe('noindex')
  })

  test('the crawlable summary is a precis of the page, not a keyword list', async ({ request }) => {
    const html = await (await request.get('/edit')).text()
    const body = html.slice(html.indexOf('<body>'))

    // Without JavaScript the container would otherwise be empty. What replaces it is the page's
    // own heading and introduction, plus links onward — nothing the rendered page does not say.
    expect(body).toContain('<h1>Update text in a PDF</h1>')
    expect(body).toContain('white box')
    expect(body).toContain('href="/merge"')
  })
})

/**
 * The administrative API.
 *
 * The key here is the one the harness bootstraps; it exists only on a server this suite started.
 */
test.describe('admin api', () => {
  const ADMIN = 'pw_e2e_test_admin_key_not_a_secret_1'

  const asAdmin = { 'X-Api-Key': ADMIN }

  test('an ordinary key cannot reach it, and is not told it exists', async ({ request }) => {
    const key = await apiKey(request)

    const response = await request.get('/v1/admin/requests', { headers: authHeaders(key) })

    // 404 rather than 403 on purpose. Confirming to a stranger that an admin API lives here
    // gains them something and costs us nothing to withhold.
    expect(response.status()).toBe(404)
    expect((await response.json()).error).toBe('not_found')
  })

  test('no key at all is refused the same way', async ({ request }) => {
    expect((await request.get('/v1/admin/requests')).status()).toBe(404)
  })

  test('the admin key is accepted and names itself', async ({ request }) => {
    const body = await (await request.get('/v1/admin/me', { headers: asAdmin })).json()

    expect(body.admin).toBe(true)
    expect(body.label).toBeTruthy()
  })

  test('requests are logged with the address that made them', async ({ request }) => {
    // Something distinctive to look for, so this does not depend on what else has run.
    await request.get('/v1/actions?probe=admin-log-test')

    const body = await (await request.get('/v1/admin/requests?take=100', { headers: asAdmin })).json()

    expect(body.total).toBeGreaterThan(0)

    const logged = body.requests.find((r: { path: string }) => r.path === '/v1/actions')

    expect(logged, 'the probe request should have been logged').toBeTruthy()
    expect(logged.address, 'an address is the whole point of the log').toBeTruthy()
    expect(logged.method).toBe('GET')
    expect(logged.statusCode).toBe(200)
  })

  test('the query string is not kept', async ({ request }) => {
    await request.get('/v1/actions?token=super-secret-value')

    const body = await (await request.get('/v1/admin/requests?take=100', { headers: asAdmin })).json()

    // Query strings carry API keys, session tokens and one-time links often enough that storing
    // them turns an audit trail into a credential store.
    expect(JSON.stringify(body.requests)).not.toContain('super-secret-value')
  })

  test('static assets are not logged', async ({ request }) => {
    await request.get('/favicon.svg')

    const body = await (await request.get('/v1/admin/requests?take=100', { headers: asAdmin })).json()
    const assets = body.requests.filter((r: { path: string }) => r.path.endsWith('.svg'))

    // One page view pulls a dozen of these. Logging them buries everything worth reading.
    expect(assets).toHaveLength(0)
  })

  test('the log can be filtered to one address', async ({ request }) => {
    const body = await (await request.get('/v1/admin/requests?take=5&address=203.0.113.99', { headers: asAdmin })).json()

    expect(Array.isArray(body.requests)).toBe(true)
    expect(body.requests).toHaveLength(0)
  })

  test('a range can be blocked, listed and unblocked', async ({ request }) => {
    const added = await request.post('/v1/admin/blocks', {
      headers: asAdmin,
      data: { cidr: '198.51.100.7/24', reason: 'end to end test' },
    })

    expect(added.ok(), await added.text()).toBeTruthy()
    const block = await added.json()

    // Host bits cleared, so the range is stored once however it was typed.
    expect(block.cidr).toBe('198.51.100.0/24')

    const listed = await (await request.get('/v1/admin/blocks', { headers: asAdmin })).json()
    expect(listed.some((b: { id: string }) => b.id === block.id)).toBe(true)

    const removed = await request.delete(`/v1/admin/blocks/${block.id}`, { headers: asAdmin })
    expect(removed.ok()).toBeTruthy()

    const after = await (await request.get('/v1/admin/blocks', { headers: asAdmin })).json()
    expect(after.some((b: { id: string }) => b.id === block.id)).toBe(false)
  })

  test('nonsense and a /0 range are both refused with an explanation', async ({ request }) => {
    const nonsense = await request.post('/v1/admin/blocks', {
      headers: asAdmin,
      data: { cidr: 'not-an-address', reason: 'x' },
    })

    expect(nonsense.status()).toBe(400)
    expect((await nonsense.json()).message).toContain('203.0.113')

    const everything = await request.post('/v1/admin/blocks', {
      headers: asAdmin,
      data: { cidr: '0.0.0.0/0', reason: 'x' },
    })

    // Blocking everything locks out the administrator doing it, which is not a thing to discover
    // by trying it.
    expect(everything.status()).toBe(400)
    expect((await everything.json()).message).toMatch(/every address/i)
  })

  test('a changed rate limit takes effect on the next request, then resets', async ({ request }) => {
    const before = await (await request.get('/v1/admin/limits', { headers: asAdmin })).json()
    const anonymous = before.find((l: { tier: string; action: string }) => l.tier === 'Anonymous' && l.action === '')

    expect(anonymous).toBeTruthy()

    try {
      const saved = await request.put('/v1/admin/limits', {
        headers: asAdmin,
        data: { ...anonymous, perMinute: 1, isOverride: true },
      })

      expect(saved.ok(), await saved.text()).toBeTruthy()

      const after = await (await request.get('/v1/admin/limits', { headers: asAdmin })).json()
      const changed = after.find((l: { tier: string; action: string }) => l.tier === 'Anonymous' && l.action === '')

      expect(changed.perMinute).toBe(1)

      // The point of storing an override is that it is visibly different from configuration.
      expect(changed.isOverride).toBe(true)
    } finally {
      // Reset even if an assertion failed, or every later anonymous test inherits a 1/min ceiling.
      await request.delete('/v1/admin/limits/Anonymous/', { headers: asAdmin })
    }

    const restored = await (await request.get('/v1/admin/limits', { headers: asAdmin })).json()
    const back = restored.find((l: { tier: string; action: string }) => l.tier === 'Anonymous' && l.action === '')

    expect(back.perMinute).toBe(anonymous.perMinute)
    expect(back.isOverride).toBe(false)
  })

  test('a limit that is not a number, tier or action is refused', async ({ request }) => {
    const badTier = await request.put('/v1/admin/limits', {
      headers: asAdmin,
      data: { tier: 'Emperor', action: '', perMinute: 5, perHour: 5, perDay: 5, concurrent: 1,
              maxUploadBytes: 1, maxPages: 1, maxBatch: 1, maxCharacters: 1, isOverride: true },
    })

    expect(badTier.status()).toBe(400)

    const negative = await request.put('/v1/admin/limits', {
      headers: asAdmin,
      data: { tier: 'Free', action: '', perMinute: -1, perHour: 5, perDay: 5, concurrent: 1,
              maxUploadBytes: 1, maxPages: 1, maxBatch: 1, maxCharacters: 1, isOverride: true },
    })

    // A negative ceiling refuses every request with a limit nobody can be under, silently.
    expect(negative.status()).toBe(400)
  })

  test('the admin routes stay out of the public API document', async ({ request }) => {
    const document = await (await request.get('/openapi/v1.json')).text()

    // Publishing the shape of the administrative surface only helps someone probing for it.
    expect(document).not.toContain('/v1/admin')
  })
})

test.describe('contact form', () => {
  /**
   * The harness configures a deliberately invalid mail key, so a valid message reaches the send
   * and is refused there. Everything up to that point is what this endpoint is responsible for,
   * and no test should depend on real mail leaving the machine.
   *
   * The contact ceiling is ten an hour, which the group as a whole would exhaust — and a rolling
   * hourly window means a second run within the hour would fail on the first test rather than the
   * last. So each test sets the ceiling it needs through the admin API, which makes the group
   * deterministic however often it runs, and exercises the limit editor while it is at it.
   */
  const ADMIN = { 'X-Api-Key': 'pw_e2e_test_admin_key_not_a_secret_1' }

  async function setContactCeiling(request: APIRequestContext, perMinute: number, perHour: number) {
    const limits = await (await request.get('/v1/admin/limits', { headers: ADMIN })).json()

    const current = limits.find(
      (l: { tier: string; action: string }) => l.tier === 'Anonymous' && l.action === 'Contact',
    )

    await request.put('/v1/admin/limits', {
      headers: ADMIN,
      data: { ...current, perMinute, perHour, perDay: 10_000 },
    })
  }

  test.beforeEach(async ({ request }) => {
    await setContactCeiling(request, 1_000, 10_000)
  })

  test.afterAll(async ({ request }) => {
    // Back to whatever the configuration says, so nothing else inherits a raised ceiling.
    await request.delete('/v1/admin/limits/Anonymous/Contact', { headers: ADMIN })
  })
  test('it reports whether it can send at all', async ({ request }) => {
    const body = await (await request.get('/v1/contact')).json()

    expect(typeof body.configured).toBe('boolean')
  })

  test('a message missing its parts is refused, each with its own reason', async ({ request }) => {
    const cases: [Record<string, string>, RegExp][] = [
      [{ name: '', email: 'ada@example.com', message: 'A perfectly ordinary message.' }, /name/i],
      [{ name: 'Ada', email: 'not-an-address', message: 'A perfectly ordinary message.' }, /email/i],
      [{ name: 'Ada', email: 'ada@example.com', message: 'short' }, /more/i],
    ]

    for (const [data, expected] of cases) {
      const response = await request.post('/v1/contact', { data })

      expect(response.status(), JSON.stringify(data)).toBe(400)
      expect((await response.json()).message).toMatch(expected)
    }
  })

  test('a filled honeypot is accepted and quietly discarded', async ({ request }) => {
    const response = await request.post('/v1/contact', {
      data: {
        name: 'Definitely A Person',
        email: 'bot@example.com',
        message: 'Buy cheap things at this address, friend.',
        website: 'http://spam.example',
      },
    })

    // Answered as though it sent. A bot told it was caught retries with the field left blank; one
    // told "thank you" goes away — and this instance has no mail configured, so a genuine send
    // would have failed with 503 rather than succeeding.
    expect(response.status()).toBe(200)
    expect((await response.json()).sent).toBe(true)
  })

  test('a valid message reaches the sender, and says so plainly when it cannot go', async ({ request }) => {
    const response = await request.post('/v1/contact', {
      data: {
        name: 'Ada Lovelace',
        email: 'ada@example.com',
        message: 'Does the form designer support radio groups across several pages?',
      },
    })

    // 200 where mail really goes out, 502 where the provider refuses (which is what the test
    // harness's deliberately invalid key produces), 503 where nothing is configured at all. What
    // must not happen is a 500, or a cheerful 200 from an instance that cannot actually send.
    expect([200, 502, 503]).toContain(response.status())

    if (response.status() !== 200) {
      expect((await response.json()).message).toMatch(/could not be sent|not configured|GitHub/i)
    }
  })

  test('it carries the same quota headers as every other action', async ({ request }) => {
    const response = await request.post('/v1/contact', {
      data: { name: 'Ada', email: 'ada@example.com', message: 'Checking the response headers.' },
    })

    expect(response.headers()['x-ratelimit-limit']).toBeTruthy()
    expect(response.headers()['x-ratelimit-remaining']).toBeTruthy()
  })

  test('it stays out of the operations catalogue', async ({ request }) => {
    const actions = await (await request.get('/v1/actions')).json()

    // It is an action for rate-limiting purposes only. Listing it beside "merge" and "watermark"
    // would misdescribe what this service does.
    expect(actions.some((a: { action: string }) => a.action === 'Contact')).toBe(false)
  })

  test('it is rate limited far more tightly than the document endpoints', async ({ request }) => {
    // Set low deliberately, rather than relying on the shipped ceiling and however much of it
    // earlier tests happened to spend.
    await setContactCeiling(request, 2, 3)

    const statuses: number[] = []

    for (let i = 0; i < 6; i++) {
      const response = await request.post('/v1/contact', {
        data: {
          name: 'Flood',
          email: 'flood@example.com',
          message: `Message number ${i}, long enough to pass validation.`,
        },
      })

      statuses.push(response.status())
    }

    // Two a minute, ten an hour. A public form that sends mail through a sender address we own is
    // the most attractive thing here to abuse, and it would otherwise be the only unmetered
    // endpoint on the service.
    expect(statuses.filter((s) => s === 429).length).toBeGreaterThan(0)
  })
})
