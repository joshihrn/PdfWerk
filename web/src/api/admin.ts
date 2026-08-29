/**
 * The admin API.
 *
 * Kept apart from the ordinary client, and so is the credential. An administrator's key is a far
 * more dangerous thing to hold than a caller's, and storing it under the same name would mean the
 * embed widget, the tool pages and anything else reading `pdfwerk.apiKey` would start sending it
 * with every request they make.
 */

const ADMIN_KEY_STORAGE = 'pdfwerk.adminKey'

export function getAdminKey(): string | null {
  return sessionStorage.getItem(ADMIN_KEY_STORAGE)
}

/**
 * Held in sessionStorage rather than localStorage, so it is gone when the tab closes.
 *
 * A key that survives indefinitely on a shared or unattended machine is the thing that turns a
 * borrowed laptop into an incident. An administrator signing in again is a small price.
 */
export function setAdminKey(key: string | null) {
  if (key) sessionStorage.setItem(ADMIN_KEY_STORAGE, key)
  else sessionStorage.removeItem(ADMIN_KEY_STORAGE)
}

export interface RequestRecord {
  id: number
  at: string
  address: string
  method: string
  path: string
  statusCode: number
  elapsedMs: number
  userAgent: string | null
  clientId: string
  action: string | null
  blocked: boolean
}

export interface IpBlock {
  id: string
  cidr: string
  reason: string
  createdAt: string
  createdBy: string
  expiresAt: string | null
  active: boolean
}

export interface LimitSetting {
  tier: string
  action: string
  perMinute: number
  perHour: number
  perDay: number
  concurrent: number
  maxUploadBytes: number
  maxPages: number
  maxBatch: number
  maxCharacters: number
  isOverride: boolean
}

export class AdminError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'AdminError'
    this.status = status
  }

  /** True when the key is missing, wrong, or not an administrator's. */
  get isUnauthorised(): boolean {
    return this.status === 404 || this.status === 401
  }
}

async function call<T>(path: string, init: RequestInit = {}): Promise<T> {
  const key = getAdminKey()

  if (!key) throw new AdminError(404, 'No administrator key is saved in this tab.')

  const response = await fetch(`/v1/admin${path}`, {
    ...init,
    headers: { ...(init.headers ?? {}), 'X-Api-Key': key },
  })

  if (!response.ok) {
    let message = response.statusText || `Request failed (${response.status})`

    try {
      message = (await response.json()).message ?? message
    } catch {
      // A non-JSON error body is not worth failing over.
    }

    // The server answers 404 for an unauthorised caller by design, so it cannot be distinguished
    // from a genuinely missing route here — and for the portal's purposes they mean the same
    // thing: this key does not open this door.
    throw new AdminError(response.status, message)
  }

  return response.status === 204 ? (undefined as T) : response.json()
}

export const admin = {
  whoAmI: () => call<{ admin: boolean; label: string }>('/me'),

  requests: (take = 100, address?: string) =>
    call<{ total: number; requests: RequestRecord[] }>(
      `/requests?take=${take}${address ? `&address=${encodeURIComponent(address)}` : ''}`,
    ),

  blocks: () => call<IpBlock[]>('/blocks'),

  block: (cidr: string, reason: string, expiresAt: string | null) =>
    call<IpBlock>('/blocks', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ cidr, reason, expiresAt }),
    }),

  unblock: (id: string) => call<{ unblocked: boolean }>(`/blocks/${id}`, { method: 'DELETE' }),

  limits: () => call<LimitSetting[]>('/limits'),

  saveLimit: (setting: LimitSetting) =>
    call<{ saved: boolean }>('/limits', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(setting),
    }),

  resetLimit: (tier: string, action: string) =>
    call<{ reset: boolean }>(`/limits/${encodeURIComponent(tier)}/${encodeURIComponent(action)}`, {
      method: 'DELETE',
    }),
}
