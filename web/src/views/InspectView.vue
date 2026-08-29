<script setup lang="ts">
import { computed, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import { PwBadge, PwButton, PwCallout, PwCard, PwPageHeader, PwSpinner } from '../components/ui'
import { api, formatBytes, type PdfInfo } from '../api/client'

const files = ref<File[]>([])
const info = ref<PdfInfo | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)

const ready = computed(() => files.value.length > 0)

async function run() {
  if (!files.value[0]) return

  busy.value = true
  error.value = null

  try {
    info.value = await api.inspect(files.value[0])
  } catch (ex) {
    info.value = null
    error.value = ex instanceof Error ? ex.message : String(ex)
  } finally {
    busy.value = false
  }
}

function formatDate(value: string | null) {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString()
}
</script>

<template>
  <div>
    <PwPageHeader
      title="Inspect a PDF"
      description="Page count, metadata, page dimensions and every form field the document contains — without modifying it."
    />

    <PwCard title="Document">
      <FileDrop v-model="files" />

      <template #footer>
        <PwButton variant="solid" :loading="busy" :disabled="!ready" @click="run">Inspect</PwButton>
      </template>
    </PwCard>

    <div v-if="busy" class="mt">
      <PwCallout tone="info"><span class="row"><PwSpinner :size="13" /> Reading the document…</span></PwCallout>
    </div>

    <div v-else-if="error" class="mt">
      <PwCallout tone="bad" title="Could not read that file">{{ error }}</PwCallout>
    </div>

    <template v-else-if="info">
      <div class="mt stack-4">
        <div class="split split--even">
          <PwCard title="Document" flush>
            <table class="table">
              <tbody>
                <tr><th scope="row">Pages</th><td>{{ info.pageCount }}</td></tr>
                <tr><th scope="row">Size</th><td>{{ formatBytes(info.byteCount) }}</td></tr>
                <tr><th scope="row">Title</th><td>{{ info.title ?? '—' }}</td></tr>
                <tr><th scope="row">Author</th><td>{{ info.author ?? '—' }}</td></tr>
                <tr><th scope="row">Subject</th><td>{{ info.subject ?? '—' }}</td></tr>
                <tr><th scope="row">Creator</th><td>{{ info.creator ?? '—' }}</td></tr>
                <tr><th scope="row">Created</th><td>{{ formatDate(info.createdAt) }}</td></tr>
                <tr>
                  <th scope="row">Form</th>
                  <td>
                    <PwBadge :tone="info.hasAcroForm ? 'accent' : 'neutral'">
                      {{ info.hasAcroForm ? `${info.fields.length} field(s)` : 'none' }}
                    </PwBadge>
                  </td>
                </tr>
                <tr>
                  <th scope="row">Encrypted</th>
                  <td>
                    <PwBadge :tone="info.isEncrypted ? 'warn' : 'neutral'">
                      {{ info.isEncrypted ? 'yes' : 'no' }}
                    </PwBadge>
                  </td>
                </tr>
              </tbody>
            </table>
          </PwCard>

          <PwCard title="Pages" description="Dimensions in points, as rendered" flush>
            <div class="scroll">
              <table class="table">
                <thead>
                  <tr>
                    <th scope="col" class="num">Page</th>
                    <th scope="col" class="num">Width</th>
                    <th scope="col" class="num">Height</th>
                    <th scope="col">Shape</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="p in info.pages" :key="p.page">
                    <td class="num">{{ p.page }}</td>
                    <td class="num">{{ p.width }}</td>
                    <td class="num">{{ p.height }}</td>
                    <td class="subtle">{{ p.width > p.height ? 'Landscape' : 'Portrait' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </PwCard>
        </div>

        <PwCard v-if="info.fields.length" title="Form fields" flush>
          <div class="scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Type</th>
                  <th scope="col" class="num">Page</th>
                  <th scope="col">Position</th>
                  <th scope="col">Value</th>
                  <th scope="col">Options</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="field in info.fields" :key="field.name">
                  <td><code>{{ field.name }}</code></td>
                  <td>{{ field.type }}</td>
                  <td class="num">{{ field.rect?.page ?? '—' }}</td>
                  <td class="subtle t-12">
                    <template v-if="field.rect">
                      {{ Math.round(field.rect.x) }}, {{ Math.round(field.rect.y) }}
                      · {{ Math.round(field.rect.width) }}×{{ Math.round(field.rect.height) }}
                    </template>
                    <template v-else>—</template>
                  </td>
                  <td>{{ field.value ?? '—' }}</td>
                  <td class="subtle t-12">{{ field.options.length ? field.options.join(', ') : '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </PwCard>
      </div>
    </template>
  </div>
</template>

<style scoped>
.mt { margin-top: var(--s-4); }

/* Long documents produce long tables; scrolling the table beats scrolling the page. */
.scroll { max-height: 420px; overflow: auto; }

.table th[scope='row'] {
  width: 120px;
  text-transform: none;
  letter-spacing: normal;
  font-size: var(--t-13);
  font-weight: var(--w-medium);
  color: var(--fg-muted);
  background: none;
}
</style>
