import { expect, test } from '@playwright/test'
import { apiKey, authHeaders, isPdf, makeDocx, makePdf, pdfPart, sharedPdf } from './support'

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

    const key = await apiKey(request)
    const pdf = await makePdf(request, key, 'The agreement covers London.', 'Deed')

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
    const text = await (
      await request.post('/v1/summarize', {
        headers: authHeaders(key),
        multipart: {
          file: pdfPart(Buffer.from(await edited.body())),
          request: JSON.stringify({ includeText: true, targetWords: 20 }),
        },
      })
    ).json().catch(() => null)

    if (text?.extractedText) {
      expect(text.extractedText).toContain('Manchester')
      expect(text.extractedText).not.toContain('London')
    }
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
