<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, getApiKey, setApiKey, type IssuedKey, type QuotaReport } from '../api/client'

const label = ref('My integration')
const issued = ref<IssuedKey | null>(null)
const savedKey = ref<string | null>(getApiKey())
const manualKey = ref('')
const quota = ref<QuotaReport | null>(null)
const identity = ref<Record<string, unknown> | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)

function announce() {
  // App.vue listens for this to refresh the tier badge.
  window.dispatchEvent(new Event('pdfwerk:key-changed'))
}

async function refresh() {
  try {
    ;[quota.value, identity.value] = await Promise.all([api.quota(), api.whoAmI()])
  } catch (ex) {
    error.value = ex instanceof Error ? ex.message : String(ex)
  }
}

onMounted(refresh)

async function create() {
  busy.value = true
  error.value = null

  try {
    const key = await api.createKey(label.value || 'self-service key')
    issued.value = key

    // Saved immediately: the secret is never retrievable again, so losing it here means
    // minting another one.
    setApiKey(key.key)
    savedKey.value = key.key
    announce()
    await refresh()
  } catch (ex) {
    error.value = ex instanceof Error ? ex.message : String(ex)
  } finally {
    busy.value = false
  }
}

function saveManual() {
  if (!manualKey.value.trim()) return
  setApiKey(manualKey.value.trim())
  savedKey.value = manualKey.value.trim()
  manualKey.value = ''
  announce()
  refresh()
}

function forget() {
  setApiKey(null)
  savedKey.value = null
  issued.value = null
  announce()
  refresh()
}

async function revoke() {
  busy.value = true
  error.value = null

  try {
    await api.revokeKey()
    forget()
  } catch (ex) {
    error.value = ex instanceof Error ? ex.message : String(ex)
  } finally {
    busy.value = false
  }
}

function masked(key: string) {
  return `${key.slice(0, 11)}${'•'.repeat(12)}${key.slice(-4)}`
}

async function copy(text: string) {
  try {
    await navigator.clipboard.writeText(text)
  } catch {
    // Clipboard access can be blocked; the value is on screen to copy by hand.
  }
}
</script>

<template>
  <div>
    <h1>API access</h1>
    <p class="muted">
      Every tool on this site is one HTTP call. A free key raises your limits and takes one click —
      no account, no email.
    </p>

    <div v-if="error" class="note err" style="margin-bottom: 16px">{{ error }}</div>

    <div class="split">
      <div class="stack">
        <div class="panel" style="margin: 0">
          <h3>Your key</h3>

          <template v-if="savedKey">
            <div class="note ok" style="display: flex; gap: 10px; align-items: center; flex-wrap: wrap">
              <code>{{ masked(savedKey) }}</code>
              <span class="tag grey">saved in this browser</span>
              <button class="btn small" style="margin-left: auto" @click="copy(savedKey!)">Copy</button>
            </div>

            <div class="btns">
              <button class="btn small" @click="forget">Forget it here</button>
              <button class="btn small danger" :disabled="busy" @click="revoke">Revoke permanently</button>
            </div>

            <p class="small muted" style="margin: 12px 0 0">
              "Forget" only clears it from this browser. "Revoke" disables it everywhere,
              immediately and permanently.
            </p>
          </template>

          <template v-else>
            <label for="label">What is it for?</label>
            <input id="label" v-model="label" type="text" placeholder="My integration" />

            <div class="btns">
              <button class="btn primary" :disabled="busy" @click="create">Create a free key</button>
            </div>

            <label for="manual" style="margin-top: 18px">…or paste one you already have</label>
            <div class="row">
              <input id="manual" v-model="manualKey" type="text" placeholder="pw_…" />
              <div style="flex: 0 0 auto">
                <button class="btn" :disabled="!manualKey.trim()" @click="saveManual">Save</button>
              </div>
            </div>
          </template>
        </div>

        <div v-if="issued" class="panel" style="margin: 0; border-color: var(--warn)">
          <h3>Copy this now</h3>
          <p class="small" style="color: var(--warn)">{{ issued.warning }}</p>
          <pre class="out" style="user-select: all">{{ issued.key }}</pre>
          <p class="small muted" style="margin: 10px 0 0">{{ issued.usage }}</p>
        </div>

        <div class="panel" style="margin: 0">
          <h3>Using it</h3>
          <pre class="out">curl -X POST http://localhost:5272/v1/create/text \
  -H 'X-Api-Key: pw_…' \
  -H 'Content-Type: application/json' \
  -d '{"content":"# Hello","format":"Markdown"}' \
  -o hello.pdf</pre>

          <p class="small muted" style="margin: 12px 0 0">
            Every response carries <code>X-RateLimit-Remaining</code> and
            <code>X-RateLimit-Reset</code>, so you can pace yourself rather than waiting for a 429.
            Full reference at <a href="/docs" target="_blank" rel="noopener">/docs</a>.
          </p>
        </div>
      </div>

      <div class="stack">
        <div class="panel" style="margin: 0">
          <h3>Current identity</h3>
          <table v-if="identity">
            <tbody>
              <tr v-for="(value, key) in identity" :key="key">
                <th style="text-transform: none">{{ key }}</th>
                <td class="small">{{ value }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-if="quota" class="panel" style="margin: 0">
          <h3>Remaining quota <span class="tag">{{ quota.tier }}</span></h3>
          <table>
            <thead><tr><th>Action</th><th>Minute</th><th>Hour</th><th>Day</th></tr></thead>
            <tbody>
              <tr v-for="row in quota.quotas" :key="row.action">
                <td class="small">{{ row.action }}</td>
                <td class="small">{{ row.remaining.minute ?? '—' }}</td>
                <td class="small">{{ row.remaining.hour ?? '—' }}</td>
                <td class="small">{{ row.remaining.day ?? '—' }}</td>
              </tr>
            </tbody>
          </table>
          <div class="btns"><button class="btn small" @click="refresh">Refresh</button></div>
        </div>
      </div>
    </div>
  </div>
</template>
