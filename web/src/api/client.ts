/**
 * Thin wrapper over the PdfWerk HTTP API.
 *
 * Deliberately not a generated client: the surface is small, and hand-writing it keeps the
 * quota headers and the download/stream distinction visible at the call site, which is where
 * the UI actually needs to reason about them.
 */

export type Delivery = 'download' | 'stream' | 'json'

export interface Quota {
  limit: number | null
  remaining: number | null
  window: string | null
}

export interface DocumentResult {
  blob: Blob
  fileName: string
  quota: Quota
  /** Which Word converter ran, when the endpoint reports one. */
  converter?: string
}

export class ApiError extends Error {
  // Declared explicitly rather than as constructor parameter properties: the project builds
  // with erasableSyntaxOnly, which forbids syntax that emits runtime code from type positions.
  readonly status: number
  readonly code: string
  readonly retryAfterSeconds?: number

  constructor(status: number, code: string, message: string, retryAfterSeconds?: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.retryAfterSeconds = retryAfterSeconds
  }

  get isRateLimit() {
    return this.status === 429
  }
}

/** The key is held in localStorage so it survives a reload; it never leaves this origin. */
const KEY_STORAGE = 'pdfwerk.apiKey'

export function getApiKey(): string | null {
  return localStorage.getItem(KEY_STORAGE)
}

export function setApiKey(key: string | null) {
  if (key) localStorage.setItem(KEY_STORAGE, key)
  else localStorage.removeItem(KEY_STORAGE)
}

function authHeaders(extra: Record<string, string> = {}): Record<string, string> {
  const key = getApiKey()
  return key ? { ...extra, 'X-Api-Key': key } : extra
}

function readQuota(response: Response): Quota {
  const num = (name: string) => {
    const raw = response.headers.get(name)
    return raw === null ? null : Number(raw)
  }

  return {
    limit: num('X-RateLimit-Limit'),
    remaining: num('X-RateLimit-Remaining'),
    window: response.headers.get('X-RateLimit-Window'),
  }
}

/** Turns a non-2xx response into an ApiError carrying the server's own explanation. */
async function toError(response: Response): Promise<ApiError> {
  let code = 'error'
  let message = response.statusText || `Request failed (${response.status})`
  let retryAfter: number | undefined

  try {
    const body = await response.json()
    code = body.error ?? code
    message = body.message ?? message
    retryAfter = body.retryAfterSeconds
  } catch {
    // A non-JSON error body is not worth failing over; the status text stands.
  }

  return new ApiError(response.status, code, message, retryAfter)
}

/** Content-Disposition carries the server's chosen name, which encodes its own conventions. */
function fileNameFrom(response: Response, fallback: string): string {
  const disposition = response.headers.get('Content-Disposition') ?? ''

  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition)
  if (utf8) return decodeURIComponent(utf8[1])

  const plain = /filename="?([^";]+)"?/i.exec(disposition)
  return plain ? plain[1] : fallback
}

async function requestDocument(
  path: string,
  init: RequestInit,
  delivery: Delivery,
  fallbackName: string,
): Promise<DocumentResult> {
  const separator = path.includes('?') ? '&' : '?'
  const response = await fetch(`${path}${separator}delivery=${delivery}`, init)

  if (!response.ok) throw await toError(response)

  return {
    blob: await response.blob(),
    fileName: fileNameFrom(response, fallbackName),
    quota: readQuota(response),
    converter: response.headers.get('X-PdfWerk-Converter') ?? undefined,
  }
}

async function requestJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, { ...init, headers: authHeaders(init.headers as Record<string, string>) })
  if (!response.ok) throw await toError(response)
  return response.json() as Promise<T>
}

// ---- shapes -------------------------------------------------------------

export interface ActionDescriptor {
  action: string
  slug: string
  title: string
  summary: string
  requiresAi: boolean
  endpoint: string
}

export interface FieldRect {
  page: number
  x: number
  y: number
  width: number
  height: number
}

export type FieldType = 'Text' | 'Checkbox' | 'RadioGroup' | 'Dropdown' | 'ListBox' | 'Signature'

export interface ExistingField {
  name: string
  type: FieldType
  rect: FieldRect | null
  value: string | null
  readOnly: boolean
  options: string[]
}

export interface PageSize {
  page: number
  width: number
  height: number
}

export interface PdfInfo {
  pageCount: number
  title: string | null
  author: string | null
  subject: string | null
  creator: string | null
  createdAt: string | null
  hasAcroForm: boolean
  isEncrypted: boolean
  byteCount: number
  fields: ExistingField[]
  pages: PageSize[]
}

export interface SummaryResult {
  summary: string
  keyPoints: string[]
  pageCount: number
  wordCount: number
  providerUsed: string
  modelUsed: string
  extractedText: string | null
}

export interface ProviderInfo {
  key: string
  model: string
  contextTokens: number
  configured: boolean
}

export interface QuotaReport {
  tier: string
  quotas: { action: string; remaining: Record<string, string> }[]
}

export interface IssuedKey {
  id: string
  label: string
  tier: string
  createdAt: string
  expiresAt: string | null
  key: string
  warning: string
  usage: string
}

// ---- operations ---------------------------------------------------------

export const api = {
  actions: () => requestJson<ActionDescriptor[]>('/v1/actions'),

  providers: () => requestJson<ProviderInfo[]>('/v1/providers'),

  quota: () => requestJson<QuotaReport>('/v1/quota'),

  whoAmI: () => requestJson<Record<string, unknown>>('/v1/keys/me'),

  createKey: (label: string) =>
    requestJson<IssuedKey>('/v1/keys', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ label }),
    }),

  revokeKey: () => requestJson<{ revoked: boolean }>('/v1/keys/me', { method: 'DELETE' }),

  createFromText: (body: Record<string, unknown>, delivery: Delivery) =>
    requestDocument(
      '/v1/create/text',
      {
        method: 'POST',
        headers: authHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify(body),
      },
      delivery,
      'document.pdf',
    ),

  createFromWord: (file: File, delivery: Delivery) => {
    const form = new FormData()
    form.append('file', file)
    return requestDocument('/v1/create/word', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'converted.pdf')
  },

  editText: (file: File, request: unknown, delivery: Delivery) => {
    const form = new FormData()
    form.append('file', file)
    form.append('request', JSON.stringify(request))
    return requestDocument('/v1/edit/text', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'edited.pdf')
  },

  merge: (files: File[], delivery: Delivery) => {
    const form = new FormData()
    files.forEach((f) => form.append('files', f))
    return requestDocument('/v1/merge', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'merged.pdf')
  },

  inspect: async (file: File): Promise<PdfInfo> => {
    const form = new FormData()
    form.append('file', file)

    const response = await fetch('/v1/inspect', { method: 'POST', headers: authHeaders(), body: form })
    if (!response.ok) throw await toError(response)
    return response.json()
  },

  designFields: (file: File, request: unknown, delivery: Delivery) => {
    const form = new FormData()
    form.append('file', file)
    form.append('request', JSON.stringify(request))
    return requestDocument('/v1/forms/design', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'form.pdf')
  },

  fillForm: (file: File, request: unknown, delivery: Delivery) => {
    const form = new FormData()
    form.append('file', file)
    form.append('request', JSON.stringify(request))
    return requestDocument('/v1/forms/fill', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'filled.pdf')
  },

  split: (file: File, request: unknown, delivery: Delivery) => {
    const form = new FormData()
    form.append('file', file)
    form.append('request', JSON.stringify(request))
    return requestDocument('/v1/split', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'split.zip')
  },

  rotate: (file: File, request: unknown, delivery: Delivery) => {
    const form = new FormData()
    form.append('file', file)
    form.append('request', JSON.stringify(request))
    return requestDocument('/v1/rotate', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'rotated.pdf')
  },

  watermark: (file: File, request: unknown, delivery: Delivery) => {
    const form = new FormData()
    form.append('file', file)
    form.append('request', JSON.stringify(request))
    return requestDocument('/v1/watermark', { method: 'POST', headers: authHeaders(), body: form }, delivery, 'watermarked.pdf')
  },

  summarize: async (file: File, request: unknown): Promise<SummaryResult> => {
    const form = new FormData()
    form.append('file', file)
    form.append('request', JSON.stringify(request))

    const response = await fetch('/v1/summarize', { method: 'POST', headers: authHeaders(), body: form })
    if (!response.ok) throw await toError(response)
    return response.json()
  },
}

/** Hands the browser a file to save. */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()

  // Revoked on the next tick so the download has already started.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`
}
