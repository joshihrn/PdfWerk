import { expect, type APIRequestContext, type Page } from '@playwright/test'

/**
 * Shared helpers.
 *
 * The important one is the API key. PdfWerk is rate limited per caller, and anonymous callers
 * share one bucket keyed on address — so a suite of a dozen tests would trip the 5/min anonymous
 * limit partway through and fail on quota rather than on behaviour. Minting one Free-tier key
 * and reusing it for the whole run raises the ceiling to 20/min and keeps failures meaningful.
 */

let cachedKey: string | null = null

export async function apiKey(request: APIRequestContext): Promise<string> {
  if (cachedKey) return cachedKey

  const response = await request.post('/v1/keys', {
    data: { label: 'playwright e2e' },
  })

  expect(response.ok(), 'could not mint an API key for the test run').toBeTruthy()

  const body = await response.json()
  cachedKey = body.key as string
  return cachedKey
}

export function authHeaders(key: string) {
  return { 'X-Api-Key': key }
}

/** A small, valid PDF produced by the service itself — no binary fixtures to keep in the repo. */
export async function makePdf(
  request: APIRequestContext,
  key: string,
  content = '# Test document\n\nGenerated for an end-to-end test.',
  title = 'Test document',
): Promise<Buffer> {
  const response = await request.post('/v1/create/text?delivery=stream', {
    headers: authHeaders(key),
    data: { content, title, format: 'Markdown', pageNumbers: false },
  })

  expect(response.ok(), 'could not create the fixture PDF').toBeTruthy()
  return Buffer.from(await response.body())
}

let cachedPdf: Buffer | null = null

/**
 * One fixture PDF for the whole run.
 *
 * Most tests only need *a* valid PDF, not a particular one, and creating a fresh one each time
 * spends CreateFromText quota that the create tests actually need. Use `makePdf` directly when
 * the content matters.
 */
export async function sharedPdf(request: APIRequestContext): Promise<Buffer> {
  if (cachedPdf) return cachedPdf

  cachedPdf = await makePdf(
    request,
    await apiKey(request),
    '# Contract\n\nSigned on behalf of both parties.\n\nThe quick brown fox jumps over the lazy dog.',
    'Shared fixture',
  )

  return cachedPdf
}

/** Multipart file part in the shape Playwright's request API expects. */
export function pdfPart(buffer: Buffer, name = 'document.pdf') {
  return { name, mimeType: 'application/pdf', buffer }
}

export function docxPart(buffer: Buffer, name = 'document.docx') {
  return {
    name,
    mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    buffer,
  }
}

/**
 * Which AI providers the server can actually reach.
 *
 * Summarisation needs a model, and a key is deliberately not committed anywhere. Tests that
 * need one skip rather than fail when none is configured, so a fresh clone runs green without
 * pretending the feature was exercised.
 */
export async function configuredProviders(request: APIRequestContext): Promise<string[]> {
  const response = await request.get('/v1/providers')
  if (!response.ok()) return []

  const body = (await response.json()) as { key: string; configured: boolean }[]
  return body.filter((p) => p.configured).map((p) => p.key)
}

export function isPdf(buffer: Buffer): boolean {
  return buffer.subarray(0, 5).toString('ascii') === '%PDF-'
}

/**
 * Puts the key into the browser's storage before any script runs, so the UI is authenticated
 * from first paint. Without it every page load starts anonymous and the tests race the limiter.
 *
 * Deliberately does not pin the theme. An init script runs on every navigation, so writing the
 * theme here would silently overwrite the user's choice on reload — which made the persistence
 * test fail against correct application behaviour.
 */
export async function signIn(page: Page, key: string) {
  await page.addInitScript((value) => {
    window.localStorage.setItem('pdfwerk.apiKey', value)
  }, key)
}

/** A minimal but structurally valid .docx, built in memory rather than committed as a fixture. */
export function makeDocx(): Buffer {
  const documentXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Quarterly Report</w:t></w:r></w:p>
<w:p><w:r><w:t>Converted by an end-to-end test.</w:t></w:r></w:p>
</w:body></w:document>`

  const contentTypes = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>`

  const rels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>`

  return zip([
    ['[Content_Types].xml', contentTypes],
    ['_rels/.rels', rels],
    ['word/document.xml', documentXml],
  ])
}

/**
 * A minimal ZIP writer, stored (uncompressed) entries only.
 *
 * Hand-rolled to keep the e2e package dependency-free — pulling in an archiver so a test can
 * build a three-file .docx is a poor trade, and stored entries are a well-specified format.
 */
function zip(entries: [string, string][]): Buffer {
  const chunks: Buffer[] = []
  const central: Buffer[] = []
  let offset = 0

  for (const [path, contents] of entries) {
    const nameBuf = Buffer.from(path, 'utf8')
    const dataBuf = Buffer.from(contents, 'utf8')
    const crc = crc32(dataBuf)

    const local = Buffer.alloc(30)
    local.writeUInt32LE(0x04034b50, 0)
    local.writeUInt16LE(20, 4)      // version needed
    local.writeUInt16LE(0, 6)       // flags
    local.writeUInt16LE(0, 8)       // stored
    local.writeUInt16LE(0, 10)      // mod time
    local.writeUInt16LE(0, 12)      // mod date
    local.writeUInt32LE(crc, 14)
    local.writeUInt32LE(dataBuf.length, 18)
    local.writeUInt32LE(dataBuf.length, 22)
    local.writeUInt16LE(nameBuf.length, 26)
    local.writeUInt16LE(0, 28)

    chunks.push(local, nameBuf, dataBuf)

    const dir = Buffer.alloc(46)
    dir.writeUInt32LE(0x02014b50, 0)
    dir.writeUInt16LE(20, 4)        // version made by
    dir.writeUInt16LE(20, 6)        // version needed
    dir.writeUInt16LE(0, 8)
    dir.writeUInt16LE(0, 10)
    dir.writeUInt16LE(0, 12)
    dir.writeUInt16LE(0, 14)
    dir.writeUInt32LE(crc, 16)
    dir.writeUInt32LE(dataBuf.length, 20)
    dir.writeUInt32LE(dataBuf.length, 24)
    dir.writeUInt16LE(nameBuf.length, 28)
    dir.writeUInt16LE(0, 30)
    dir.writeUInt16LE(0, 32)
    dir.writeUInt16LE(0, 34)
    dir.writeUInt16LE(0, 36)
    dir.writeUInt32LE(0, 38)
    dir.writeUInt32LE(offset, 42)

    central.push(dir, nameBuf)
    offset += local.length + nameBuf.length + dataBuf.length
  }

  const centralBuf = Buffer.concat(central)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0)
  end.writeUInt16LE(0, 4)
  end.writeUInt16LE(0, 6)
  end.writeUInt16LE(entries.length, 8)
  end.writeUInt16LE(entries.length, 10)
  end.writeUInt32LE(centralBuf.length, 12)
  end.writeUInt32LE(offset, 16)
  end.writeUInt16LE(0, 20)

  return Buffer.concat([...chunks, centralBuf, end])
}

const CRC_TABLE = (() => {
  const table = new Uint32Array(256)
  for (let i = 0; i < 256; i++) {
    let c = i
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    table[i] = c >>> 0
  }
  return table
})()

function crc32(buffer: Buffer): number {
  let crc = 0xffffffff
  for (const byte of buffer) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8)
  return (crc ^ 0xffffffff) >>> 0
}

/**
 * A PDF that already carries form fields, built through the API rather than the designer.
 *
 * Fill mode needs a document with fields in it, and going through the browser designer first
 * would make a fill failure look like a designer failure.
 */
export async function makeFormPdf(request: APIRequestContext): Promise<Buffer> {
  const key = await apiKey(request)
  const pdf = await sharedPdf(request)

  const response = await request.post('/v1/forms/design?delivery=stream', {
    headers: authHeaders(key),
    multipart: {
      file: pdfPart(pdf),
      request: JSON.stringify({
        add: [
          { name: 'clientName', type: 'Text', rect: { page: 1, x: 72, y: 300, width: 240, height: 22 } },
          { name: 'agreed', type: 'Checkbox', rect: { page: 1, x: 72, y: 260, width: 16, height: 16 } },
        ],
      }),
    },
  })

  expect(response.ok(), 'could not build a form fixture').toBeTruthy()
  return Buffer.from(await response.body())
}

let cachedMultiPage: Buffer | null = null

/**
 * A document of several pages, for anything that only misbehaves past the first one — page
 * selection in the designer, ranges, rotation of specific pages.
 */
export async function multiPagePdf(request: APIRequestContext): Promise<Buffer> {
  if (cachedMultiPage) return cachedMultiPage

  const paragraph = 'The quick brown fox jumps over the lazy dog. '.repeat(70)
  const content = ['# One', paragraph, '# Two', paragraph, '# Three', paragraph].join('\n\n')

  cachedMultiPage = await makePdf(request, await apiKey(request), content, 'Multi page fixture')
  return cachedMultiPage
}
