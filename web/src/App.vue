<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { api } from './api/client'
import { PwBadge } from './components/ui'
import ConsentBanner from './components/ConsentBanner.vue'
import { analyticsAvailable, reconsider, restore } from './analytics'

/**
 * The application shell: a single top bar with product, navigation and account state.
 *
 * No sidebar. There are ten tools and no hierarchy between them, so a sidebar would spend
 * 240px of every screen restating a flat list — and the working area is where the PDF preview
 * needs to live.
 */

const consent = ref<InstanceType<typeof ConsentBanner> | null>(null)
const analyticsOffered = analyticsAvailable()

// Reloads analytics for a visitor who already accepted, before anything else runs. A previous
// yes should not have to be given again on every visit.
restore()

/** Clears the stored decision and shows the banner, so a choice can be taken back. */
function askAgain() {
  reconsider()
  consent.value?.open()
}

const tier = ref<string>('')
const reachable = ref(true)

type Theme = 'system' | 'light' | 'dark'
const theme = ref<Theme>((localStorage.getItem('pdfwerk.theme') as Theme) ?? 'system')

watch(
  theme,
  (value, previous) => {
    const root = document.documentElement

    // Only for an actual switch, not the initial application — suppressing on first paint
    // would be pointless, and the class would linger if rAF never runs.
    if (previous !== undefined) root.classList.add('theme-switching')

    // Absence of the attribute means "follow the OS", which the token layer keys off.
    if (value === 'system') root.removeAttribute('data-theme')
    else root.setAttribute('data-theme', value)

    localStorage.setItem('pdfwerk.theme', value)

    if (previous !== undefined) {
      // Force the new values to be computed before transitions are allowed back.
      void root.offsetWidth
      requestAnimationFrame(() => root.classList.remove('theme-switching'))
    }
  },
  { immediate: true },
)

function cycleTheme() {
  theme.value = theme.value === 'system' ? 'light' : theme.value === 'light' ? 'dark' : 'system'
}

/**
 * The tier badge doubles as the "is my key working" indicator. A key that silently fails
 * validation is otherwise indistinguishable from having no key at all.
 */
async function refreshTier() {
  try {
    tier.value = (await api.quota()).tier
    reachable.value = true
  } catch {
    reachable.value = false
  }
}

onMounted(refreshTier)
window.addEventListener('pdfwerk:key-changed', refreshTier)

const nav = [
  { to: '/create', label: 'Create' },
  { to: '/word', label: 'Word' },
  { to: '/edit', label: 'Edit text' },
  { to: '/annotate', label: 'Add text' },
  { to: '/forms', label: 'Forms' },
  { to: '/merge', label: 'Merge' },
  { to: '/pages', label: 'Pages' },
  { to: '/summarize', label: 'Summarise' },
  { to: '/inspect', label: 'Inspect' },
]
</script>

<template>
  <a class="skip-link" href="#main">Skip to content</a>

  <header class="app-nav">
    <div class="app-nav__inner">
      <RouterLink to="/" class="brand" aria-label="PdfWerk home">
        <!--
          Inlined rather than loaded from /brand/mark.svg: the header is the first thing painted,
          and a logo that arrives one request later is a visible flinch on every page load. The
          file in public/brand is the same drawing, for everywhere outside this app.
        -->
        <svg class="brand__mark" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <g fill="none" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
            <path class="brand__brackets" d="M6.4 3.4H3.6v17.2h2.8M17.6 3.4h2.8v17.2h-2.8" />
            <path stroke="currentColor" d="M8.6 6.4h4.6L15.8 9v9H8.6z" />
            <path stroke="currentColor" d="M13.2 6.4V9h2.6" />
          </g>
        </svg>
        <span class="brand__name">Pdf<span class="brand__accent">Werk</span></span>
      </RouterLink>

      <nav class="app-nav__links" aria-label="Tools">
        <RouterLink v-for="item in nav" :key="item.to" :to="item.to">{{ item.label }}</RouterLink>
      </nav>

      <div class="app-nav__end">
        <!--
          Contact sits in the header rather than only in the footer. Someone deciding whether to
          trust a tool with their document wants to see there is a person behind it before they
          upload anything, and the footer is below the fold on every tool page.
        -->
        <RouterLink to="/contact" class="app-nav__contact">
          <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
            <rect x="1.6" y="3.4" width="12.8" height="9.2" rx="1.4"
                  fill="none" stroke="currentColor" stroke-width="1.3" />
            <path d="M2 4.4 8 8.8l6-4.4" fill="none" stroke="currentColor"
                  stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
          <span>Contact</span>
        </RouterLink>

        <a class="app-nav__doc" href="/docs" target="_blank" rel="noopener">API<span aria-hidden="true"> ↗</span></a>

        <RouterLink to="/api" class="app-nav__tier">
          <PwBadge v-if="!reachable" tone="bad" dot>offline</PwBadge>
          <PwBadge v-else-if="tier === 'Anonymous'" tone="neutral">Anonymous</PwBadge>
          <PwBadge v-else-if="tier" tone="ok" dot>{{ tier }}</PwBadge>
        </RouterLink>

        <button
          type="button"
          class="app-nav__theme"
          :title="`Theme: ${theme}`"
          :aria-label="`Theme: ${theme}. Change theme.`"
          @click="cycleTheme"
        >
          <svg v-if="theme === 'light'" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
            <circle cx="8" cy="8" r="3.2" fill="none" stroke="currentColor" stroke-width="1.3" />
            <path d="M8 1v1.6M8 13.4V15M15 8h-1.6M2.6 8H1M12.9 3.1l-1.1 1.1M4.2 11.8l-1.1 1.1M12.9 12.9l-1.1-1.1M4.2 4.2 3.1 3.1"
                  stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
          </svg>
          <svg v-else-if="theme === 'dark'" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
            <path d="M13.5 9.6A5.8 5.8 0 0 1 6.4 2.5a5.8 5.8 0 1 0 7.1 7.1Z" fill="none"
                  stroke="currentColor" stroke-width="1.3" stroke-linejoin="round" />
          </svg>
          <svg v-else viewBox="0 0 16 16" aria-hidden="true" focusable="false">
            <rect x="1.8" y="3" width="12.4" height="8.4" rx="1.2" fill="none"
                  stroke="currentColor" stroke-width="1.3" />
            <path d="M5.5 13.6h5" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
          </svg>
        </button>
      </div>
    </div>
  </header>

  <main id="main" class="app-main">
    <RouterView />
  </main>

  <ConsentBanner ref="consent" />

  <footer class="app-footer">
    <div class="app-footer__inner">
      <div class="app-footer__brand">
        <svg class="app-footer__mark" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <g fill="none" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
            <path class="brand__brackets" d="M6.4 3.4H3.6v17.2h2.8M17.6 3.4h2.8v17.2h-2.8" />
            <path stroke="currentColor" d="M8.6 6.4h4.6L15.8 9v9H8.6z" />
            <path stroke="currentColor" d="M13.2 6.4V9h2.6" />
          </g>
        </svg>
        <span>PdfWerk</span>
      </div>

      <nav class="app-footer__links" aria-label="Site">
        <!--
          The footer, not the header. The header nav is labelled "Tools" and already scrolls on a
          narrow screen; the embed demo is documentation, and this is where the other
          documentation-shaped links live.
        -->
        <a href="/embed-demo.html" target="_blank" rel="noopener">Embed widgets</a>
        <RouterLink to="/contact">Contact</RouterLink>
        <RouterLink to="/privacy">Privacy</RouterLink>
        <RouterLink to="/terms">Terms</RouterLink>
        <a href="/docs" target="_blank" rel="noopener">API reference</a>
        <a href="https://github.com/joshihrn/PdfWerk/blob/main/LICENSING.md" target="_blank" rel="noopener">BSL 1.1</a>
        <a href="https://github.com/joshihrn/PdfWerk" target="_blank" rel="noopener">GitHub</a>
        <button v-if="analyticsOffered" type="button" class="app-footer__link" @click="askAgain">
          Cookies
        </button>
      </nav>
    </div>
  </footer>
</template>

<style scoped>
.app-nav {
  position: sticky;
  top: 0;
  z-index: 20;
  background: var(--bg-raised);
  border-bottom: 1px solid var(--border);
}

.app-nav__inner {
  max-width: var(--page-max);
  margin: 0 auto;
  padding: 0 var(--s-6);
  height: 52px;
  display: flex;
  align-items: center;
  gap: var(--s-6);
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: var(--s-2);
  color: var(--fg);
  font-weight: var(--w-semi);
  font-size: var(--t-14);
  letter-spacing: var(--track-snug);
  flex: none;
}

.brand:hover { text-decoration: none; }

/* The page follows the surrounding text so the mark sits at the same weight as the wordmark;
   only the brackets carry the accent. */
.brand__mark { width: 20px; height: 20px; color: var(--fg); flex: none; }
.brand__brackets { stroke: var(--link); }
.brand__accent { color: var(--link); }

.app-nav__links {
  display: flex;
  align-items: center;
  gap: 2px;
  flex: 1 1 auto;
  min-width: 0;
  overflow-x: auto;
  scrollbar-width: none;
}

.app-nav__links::-webkit-scrollbar { display: none; }

.app-nav__links a {
  padding: var(--s-1) var(--s-2);
  border-radius: var(--r-sm);
  color: var(--fg-muted);
  font-size: var(--t-13);
  white-space: nowrap;
}

.app-nav__links a:hover {
  background: var(--bg-hover);
  color: var(--fg);
  text-decoration: none;
}

/* The active link is marked by weight and colour rather than a background block, which
   would compete with the segmented controls inside the page. */
.app-nav__links a.router-link-active {
  color: var(--fg);
  font-weight: var(--w-medium);
}

.app-nav__end {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  flex: none;
}

.app-nav__doc {
  font-size: var(--t-13);
  color: var(--fg-muted);
}

/*
 * A bordered control rather than another muted nav link.
 *
 * Sitting among the tool links it read as one more tool, which is the opposite of the point: it
 * is how you reach a person, and someone deciding whether to trust this with a document wants to
 * see that before they upload anything. Outlined rather than solid so it still loses to the
 * primary action on each page.
 */
.app-nav__contact {
  display: inline-flex;
  align-items: center;
  gap: var(--s-2);
  padding: 5px var(--s-3);
  font-size: var(--t-13);
  font-weight: var(--w-medium);
  color: var(--link);
  background: color-mix(in srgb, var(--link) 8%, transparent);
  border: 1px solid color-mix(in srgb, var(--link) 42%, transparent);
  border-radius: var(--r-md);
  white-space: nowrap;
}

.app-nav__contact svg { width: 15px; height: 15px; }

.app-nav__contact:hover {
  color: var(--fg);
  background: color-mix(in srgb, var(--link) 16%, transparent);
  border-color: var(--link);
  text-decoration: none;
}

.app-nav__contact.router-link-active {
  background: color-mix(in srgb, var(--link) 18%, transparent);
  border-color: var(--link);
}

.app-nav__tier:hover { text-decoration: none; }

.app-nav__theme {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: var(--bg-raised);
  color: var(--fg-muted);
  cursor: pointer;
  transition: background-color var(--fast) var(--ease), color var(--fast) var(--ease);
}

.app-nav__theme:hover { background: var(--bg-hover); color: var(--fg); }
.app-nav__theme svg { width: 14px; height: 14px; }

.app-main {
  max-width: var(--page-max);
  margin: 0 auto;
  padding: var(--s-8) var(--s-6) var(--s-16);
}

.app-footer {
  border-top: 1px solid var(--border);
  background: var(--bg-raised);
}

.app-footer__inner {
  max-width: var(--page-max);
  margin: 0 auto;
  padding: var(--s-5) var(--s-6);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--s-4) var(--s-6);
  font-size: var(--t-12);
  color: var(--fg-subtle);
  flex-wrap: wrap;
}

.app-footer__brand {
  display: flex;
  align-items: center;
  gap: var(--s-2);
  color: var(--fg-muted);
  font-weight: var(--w-medium);
}

.app-footer__mark { width: 16px; height: 16px; color: var(--fg-muted); flex: none; }

.app-footer__links {
  display: flex;
  align-items: center;
  gap: var(--s-4);
  flex-wrap: wrap;
}

/* The cookie control is a button because it changes state rather than navigating, but it must
   sit in the row as though it were another link. */
.app-footer__link {
  background: none;
  border: 0;
  padding: 0;
  font: inherit;
  color: var(--fg-subtle);
  cursor: pointer;
}

.app-footer__link:hover { color: var(--fg-muted); text-decoration: underline; }

.app-footer__inner a { color: var(--fg-subtle); }
.app-footer__inner a:hover { color: var(--fg-muted); }

@media (max-width: 720px) {
  .app-nav__inner { padding: 0 var(--s-4); gap: var(--s-3); }
  .app-main { padding: var(--s-6) var(--s-4) var(--s-12); }
  .app-nav__doc { display: none; }

  /* The label goes, the envelope stays. Dropping contact entirely on a phone would remove it
     from exactly the visitors least likely to scroll to the footer for it. */
  .app-nav__contact span { display: none; }
  .app-nav__contact { padding: 5px var(--s-2); }
}
</style>
