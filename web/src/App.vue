<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, getApiKey } from './api/client'

const tier = ref<string>('…')

/**
 * The tier badge is the quickest way for someone to tell whether their key is actually being
 * accepted — a key that silently fails validation is otherwise indistinguishable from no key.
 */
async function refreshTier() {
  try {
    const report = await api.quota()
    tier.value = report.tier
  } catch {
    tier.value = 'offline'
  }
}

onMounted(refreshTier)

// The API view mints and clears keys, so the badge listens for its own storage key changing.
window.addEventListener('storage', refreshTier)
window.addEventListener('pdfwerk:key-changed', refreshTier)
</script>

<template>
  <div class="shell">
    <nav class="nav">
      <RouterLink to="/" class="brand">
        <span class="dot"></span> PdfWerk
      </RouterLink>

      <div class="links">
        <RouterLink to="/create">Create</RouterLink>
        <RouterLink to="/word">Word</RouterLink>
        <RouterLink to="/edit">Edit text</RouterLink>
        <RouterLink to="/forms">Forms</RouterLink>
        <RouterLink to="/merge">Merge</RouterLink>
        <RouterLink to="/pages">Pages</RouterLink>
        <RouterLink to="/summarize">Summarise</RouterLink>
        <RouterLink to="/inspect">Inspect</RouterLink>
        <RouterLink to="/api">API</RouterLink>
        <a href="/docs" target="_blank" rel="noopener">Docs ↗</a>
      </div>

      <span class="tag" :class="{ grey: tier === 'Anonymous' || tier === 'offline' }" :title="getApiKey() ? 'Using your saved API key' : 'No API key saved'">
        {{ tier }}
      </span>
    </nav>

    <RouterView />
  </div>
</template>
