<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { api } from './api/client'
import { PwBadge } from './components/ui'

/**
 * The application shell: a single top bar with product, navigation and account state.
 *
 * No sidebar. There are ten tools and no hierarchy between them, so a sidebar would spend
 * 240px of every screen restating a flat list — and the working area is where the PDF preview
 * needs to live.
 */

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
        <svg class="brand__mark" viewBox="0 0 20 20" aria-hidden="true" focusable="false">
          <rect x="2.5" y="1.5" width="12" height="17" rx="1.5" fill="none"
                stroke="currentColor" stroke-width="1.4" />
          <path d="M5.5 6.5h6M5.5 9.5h6M5.5 12.5h3.5" stroke="currentColor"
                stroke-width="1.4" stroke-linecap="round" />
        </svg>
        <span class="brand__name">PdfWerk</span>
      </RouterLink>

      <nav class="app-nav__links" aria-label="Tools">
        <RouterLink v-for="item in nav" :key="item.to" :to="item.to">{{ item.label }}</RouterLink>
      </nav>

      <div class="app-nav__end">
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

  <footer class="app-footer">
    <div class="app-footer__inner">
      <span>PdfWerk</span>
      <span class="app-footer__sep" aria-hidden="true">·</span>
      <a href="https://github.com/joshihrn/PdfWerk/blob/main/LICENSING.md" target="_blank" rel="noopener">BSL 1.1</a>
      <span class="app-footer__sep" aria-hidden="true">·</span>
      <a href="/docs" target="_blank" rel="noopener">API reference</a>
      <span class="app-footer__sep" aria-hidden="true">·</span>
      <a href="https://github.com/joshihrn/PdfWerk" target="_blank" rel="noopener">GitHub</a>
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
.brand__mark { width: 18px; height: 18px; color: var(--fg-muted); }

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
  padding: var(--s-4) var(--s-6);
  display: flex;
  align-items: center;
  gap: var(--s-2);
  font-size: var(--t-12);
  color: var(--fg-subtle);
  flex-wrap: wrap;
}

.app-footer__inner a { color: var(--fg-subtle); }
.app-footer__inner a:hover { color: var(--fg-muted); }
.app-footer__sep { opacity: 0.5; }

@media (max-width: 720px) {
  .app-nav__inner { padding: 0 var(--s-4); gap: var(--s-3); }
  .app-main { padding: var(--s-6) var(--s-4) var(--s-12); }
  .app-nav__doc { display: none; }
}
</style>
