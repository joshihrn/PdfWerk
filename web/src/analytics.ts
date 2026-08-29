/**
 * Google Analytics, loaded only after the visitor says yes.
 *
 * Analytics cookies are the kind that need consent before they are set — there is no
 * legitimate-interest route for them in the UK or the EU. So nothing here runs at import time:
 * the gtag script is not fetched, no cookie is written and no request reaches Google until
 * `enable()` is called, which only happens on an explicit accept or on a stored prior accept.
 *
 * The measurement ID comes from the server, so a self-hosted copy reports to its own property, or
 * to none at all, without a rebuild.
 */

const DECISION = 'pdfwerk.analytics'

export type Decision = 'accepted' | 'declined' | null

declare global {
  interface Window {
    dataLayer?: unknown[]
    gtag?: (...args: unknown[]) => void
  }
}

let loaded = false

export function getDecision(): Decision {
  const stored = localStorage.getItem(DECISION)
  return stored === 'accepted' || stored === 'declined' ? stored : null
}

/** The property to report to, written into the page by the server. Empty means analytics is off. */
export function measurementId(): string {
  return document.querySelector<HTMLMetaElement>('meta[name="pdfwerk:analytics"]')?.content?.trim() ?? ''
}

export function analyticsAvailable(): boolean {
  return measurementId().length > 0
}

/** Injects gtag.js. Safe to call more than once; it only ever loads the script once. */
function load(id: string) {
  if (loaded) return
  loaded = true

  window.dataLayer = window.dataLayer || []
  window.gtag = function gtag() {
    // Must push `arguments` itself rather than an array of them: gtag.js reads the arguments
    // object, and spreading it here produces events Google silently ignores.
    // eslint-disable-next-line prefer-rest-params
    window.dataLayer!.push(arguments)
  }

  window.gtag('js', new Date())

  window.gtag('config', id, {
    // The SPA sends its own page_view on each route change, so the automatic one would double
    // count the landing page and miss every page after it.
    send_page_view: false,
    anonymize_ip: true,
  })

  const script = document.createElement('script')
  script.async = true
  script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(id)}`
  document.head.appendChild(script)
}

export function enable() {
  const id = measurementId()
  if (!id) return

  localStorage.setItem(DECISION, 'accepted')
  load(id)
  page(window.location.pathname)
}

/**
 * Records the refusal and does nothing else.
 *
 * Storing "declined" is what makes the banner stop asking. It is not a tracking cookie: it holds
 * one word, is never sent anywhere, and exists only so the answer is respected.
 */
export function decline() {
  localStorage.setItem(DECISION, 'declined')
}

/** Forgets the decision so the banner appears again. Reached from the footer. */
export function reconsider() {
  localStorage.removeItem(DECISION)
}

export function page(path: string) {
  if (!loaded || !window.gtag) return

  window.gtag('event', 'page_view', {
    page_path: path,
    page_location: window.location.origin + path,
    page_title: document.title,
  })
}

/** Loads analytics on start-up only if this visitor has already accepted. */
export function restore() {
  if (getDecision() === 'accepted' && analyticsAvailable()) load(measurementId())
}
