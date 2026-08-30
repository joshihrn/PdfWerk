<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  PwBadge,
  PwButton,
  PwCallout,
  PwCard,
  PwField,
  PwInput,
  PwPageHeader,
} from '../components/ui'
import { api, getApiKey, setApiKey, type IssuedKey, type QuotaReport } from '../api/client'

const label = ref('My integration')
const issued = ref<IssuedKey | null>(null)
const savedKey = ref<string | null>(getApiKey())
const manualKey = ref('')
const quota = ref<QuotaReport | null>(null)
const identity = ref<Record<string, unknown> | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)
const copied = ref(false)

const masked = computed(() =>
  savedKey.value ? `${savedKey.value.slice(0, 11)}${'•'.repeat(14)}${savedKey.value.slice(-4)}` : '',
)

/** App.vue listens for this to refresh the tier badge in the nav. */
function announce() {
  window.dispatchEvent(new Event('pdfwerk:key-changed'))
}

async function refresh() {
  try {
    ;[quota.value, identity.value] = await Promise.all([api.quota(), api.whoAmI()])
    error.value = null
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
  const value = manualKey.value.trim()
  if (!value) return

  setApiKey(value)
  savedKey.value = value
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

async function copy(text: string) {
  try {
    await navigator.clipboard.writeText(text)
    copied.value = true
    setTimeout(() => (copied.value = false), 1600)
  } catch {
    // Clipboard access can be blocked; the value is on screen to copy by hand.
  }
}
</script>

<template>
  <div>
    <PwPageHeader
      title="API access"
      description="Every tool on this site is one HTTP call. A free key raises your limits and takes one request — no account, no email."
    />

    <PwCallout v-if="error" tone="bad" title="Something went wrong" class="mb">{{ error }}</PwCallout>

    <div class="split">
      <div class="stack-4">
        <PwCard title="Your key">
          <template v-if="savedKey">
            <div class="key">
              <code class="key__value">{{ masked }}</code>
              <PwBadge tone="neutral">saved in this browser</PwBadge>
              <PwButton size="sm" class="right" @click="copy(savedKey!)">
                {{ copied ? 'Copied' : 'Copy' }}
              </PwButton>
            </div>

            <p class="t-12 subtle" style="margin-top: var(--s-3)">
              <strong>Forget</strong> clears it from this browser only.
              <strong>Revoke</strong> disables it everywhere, immediately and permanently.
            </p>
          </template>

          <template v-else>
            <div class="stack-4">
              <PwField v-slot="{ id }" label="What is it for?" help="Shown when you inspect the key later">
                <PwInput :id="id" v-model="label" placeholder="My integration" />
              </PwField>

              <PwButton variant="solid" :loading="busy" @click="create">Create a free key</PwButton>

              <div class="or"><span>or paste one you already have</span></div>

              <div class="row">
                <PwField v-slot="{ id }" label="Existing key" hide-label class="grow">
                  <PwInput :id="id" v-model="manualKey" mono placeholder="pw_…" />
                </PwField>
                <PwButton :disabled="!manualKey.trim()" @click="saveManual">Save</PwButton>
              </div>
            </div>
          </template>

          <template v-if="savedKey" #footer>
            <PwButton size="sm" @click="forget">Forget it here</PwButton>
            <PwButton variant="danger" size="sm" :loading="busy" @click="revoke">
              Revoke permanently
            </PwButton>
          </template>
        </PwCard>

        <PwCard v-if="issued" title="Copy this now" class="urgent">
          <PwCallout tone="warn">{{ issued.warning }}</PwCallout>
          <pre class="secret">{{ issued.key }}</pre>
          <p class="t-12 subtle" style="margin-top: var(--s-2)">{{ issued.usage }}</p>
        </PwCard>

        <PwCard title="Using it">
          <pre><code>curl -X POST http://localhost:5272/v1/create/text \
  -H 'X-Api-Key: pw_…' \
  -H 'Content-Type: application/json' \
  -d '{"content":"# Hello","format":"Markdown"}' \
  -o hello.pdf</code></pre>

          <p class="t-12 subtle" style="margin-top: var(--s-3)">
            Every response carries <code>X-RateLimit-Remaining</code> and
            <code>X-RateLimit-Reset</code>, so you can pace requests rather than waiting for a 429.
            Full reference at <a href="/docs" target="_blank" rel="noopener">/docs</a>.
          </p>
        </PwCard>

        <PwCard title="Embedding a tool in your own page">
          <p class="t-13 subtle" style="margin-top: 0">
            One script tag drops a working tool into any site. Each widget renders inside a shadow
            root, so your styles cannot leak in and its styles cannot leak out.
          </p>

          <pre><code>&lt;div id="pdf-create"&gt;&lt;/div&gt;
&lt;script src="/pdfwerk-embed.js"&gt;&lt;/script&gt;
&lt;script&gt;
  PdfWerk.mount('#pdf-create', { tool: 'create', apiKey: 'pw_…' })
&lt;/script&gt;</code></pre>

          <p class="t-13 subtle" style="margin-top: var(--s-3)">
            <a href="/embed-demo.html" target="_blank" rel="noopener">Open the live examples</a> —
            every tool running against this server, with the options table and a log of what the
            callbacks receive. The page is deliberately styled with clashing fonts and colours to
            show that none of it reaches inside the widgets.
          </p>
        </PwCard>
      </div>

      <div class="stack-4">
        <PwCard v-if="identity" title="Current identity" flush>
          <table class="table">
            <tbody>
              <tr v-for="(value, key) in identity" :key="key">
                <th scope="row">{{ key }}</th>
                <td class="t-12">{{ value }}</td>
              </tr>
            </tbody>
          </table>
        </PwCard>

        <PwCard v-if="quota" title="Remaining quota" flush>
          <template #actions>
            <PwBadge :tone="quota.tier === 'Anonymous' ? 'neutral' : 'ok'">{{ quota.tier }}</PwBadge>
          </template>

          <table class="table">
            <thead>
              <tr>
                <th scope="col">Action</th>
                <th scope="col" class="num">Minute</th>
                <th scope="col" class="num">Hour</th>
                <th scope="col" class="num">Day</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in quota.quotas" :key="row.action">
                <td>{{ row.action }}</td>
                <td class="num">{{ row.remaining.minute ?? '—' }}</td>
                <td class="num">{{ row.remaining.hour ?? '—' }}</td>
                <td class="num">{{ row.remaining.day ?? '—' }}</td>
              </tr>
            </tbody>
          </table>

          <template #footer>
            <PwButton size="sm" @click="refresh">Refresh</PwButton>
            <span class="t-12 subtle">Checking consumes nothing</span>
          </template>
        </PwCard>
      </div>
    </div>
  </div>
</template>

<style scoped>
.mb { margin-bottom: var(--s-4); }

.key {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  flex-wrap: wrap;
}

.key__value {
  font-size: var(--t-13);
  padding: var(--s-1) var(--s-2);
}

.urgent { border-color: var(--warn-bd); }

.secret {
  margin-top: var(--s-3);
  user-select: all;
  font-size: var(--t-13);
  word-break: break-all;
  white-space: pre-wrap;
}

/* A labelled rule, so the two ways of getting a key read as alternatives. */
.or {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  font-size: var(--t-12);
  color: var(--fg-subtle);
}

.or::before,
.or::after {
  content: '';
  flex: 1 1 auto;
  height: 1px;
  background: var(--border);
}

.table th[scope='row'] {
  text-transform: none;
  letter-spacing: normal;
  font-size: var(--t-12);
  font-weight: var(--w-medium);
  color: var(--fg-muted);
  background: none;
  width: 130px;
}
</style>
