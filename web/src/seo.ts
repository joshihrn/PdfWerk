import seo from '../public/seo.json'

/**
 * Keeps the document's metadata in step with the route.
 *
 * The server writes the right title, description and canonical into the HTML it serves, so a
 * cold load is already correct — but a single-page app changes route without a request, and
 * without this the tab would keep the title of whichever page was loaded first. That matters for
 * more than tidiness: it is what browser history, bookmarks and shared links all read from.
 *
 * Imported from `public/` rather than duplicated here. The file is copied verbatim into wwwroot
 * for the server to read, and bundled into the app at build time, so both sides answer from one
 * source and cannot drift.
 */

interface SeoPage {
  path: string
  title: string
  description: string
}

const pages = new Map<string, SeoPage>(seo.routes.map((route) => [route.path, route as SeoPage]))

/** Sets a named meta tag's content, creating the tag if the shell did not carry one. */
function setMeta(selector: string, attribute: 'name' | 'property', key: string, content: string) {
  let tag = document.head.querySelector<HTMLMetaElement>(selector)

  if (!tag) {
    tag = document.createElement('meta')
    tag.setAttribute(attribute, key)
    document.head.appendChild(tag)
  }

  tag.content = content
}

export function applyRouteMetadata(path: string) {
  const page = pages.get(path === '/' ? '/' : path.replace(/\/+$/, '')) ?? pages.get('/')
  if (!page) return

  document.title = page.title

  setMeta('meta[name="description"]', 'name', 'description', page.description)
  setMeta('meta[property="og:title"]', 'property', 'og:title', page.title)
  setMeta('meta[property="og:description"]', 'property', 'og:description', page.description)

  // Canonical is absolute and comes from the live origin rather than the configured one, so a
  // staging deployment does not announce production URLs as its own.
  const canonical =
    document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]') ??
    document.head.appendChild(Object.assign(document.createElement('link'), { rel: 'canonical' }))

  canonical.href = new URL(page.path, window.location.origin).toString()
}
