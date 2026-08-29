<script setup lang="ts">
import { ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import { api, formatBytes, type PdfInfo } from '../api/client'

const files = ref<File[]>([])
const info = ref<PdfInfo | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)

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
</script>

<template>
  <div>
    <h1>Inspect a PDF</h1>
    <p class="muted">
      Page count, metadata, page dimensions and every form field the document contains — without
      modifying it.
    </p>

    <div class="panel">
      <FileDrop v-model="files" />
      <div class="btns">
        <button class="btn primary" :disabled="busy || !files.length" @click="run">Inspect</button>
      </div>
    </div>

    <div v-if="busy" class="note info"><span class="spinner"></span> Reading…</div>
    <div v-else-if="error" class="note err">{{ error }}</div>

    <template v-else-if="info">
      <div class="panel">
        <h3>Document</h3>
        <table>
          <tbody>
            <tr><th>Pages</th><td>{{ info.pageCount }}</td></tr>
            <tr><th>Size</th><td>{{ formatBytes(info.byteCount) }}</td></tr>
            <tr><th>Title</th><td>{{ info.title ?? '—' }}</td></tr>
            <tr><th>Author</th><td>{{ info.author ?? '—' }}</td></tr>
            <tr><th>Creator</th><td>{{ info.creator ?? '—' }}</td></tr>
            <tr><th>Created</th><td>{{ info.createdAt ? new Date(info.createdAt).toLocaleString() : '—' }}</td></tr>
            <tr>
              <th>Form</th>
              <td>
                <span class="tag" :class="{ grey: !info.hasAcroForm }">
                  {{ info.hasAcroForm ? `${info.fields.length} field(s)` : 'none' }}
                </span>
              </td>
            </tr>
            <tr>
              <th>Encrypted</th>
              <td><span class="tag" :class="{ grey: !info.isEncrypted }">{{ info.isEncrypted ? 'yes' : 'no' }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="info.fields.length" class="panel">
        <h3>Form fields</h3>
        <table>
          <thead>
            <tr><th>Name</th><th>Type</th><th>Page</th><th>Position</th><th>Value</th></tr>
          </thead>
          <tbody>
            <tr v-for="field in info.fields" :key="field.name">
              <td><code>{{ field.name }}</code></td>
              <td>{{ field.type }}</td>
              <td>{{ field.rect?.page ?? '—' }}</td>
              <td class="muted small">
                <template v-if="field.rect">
                  {{ Math.round(field.rect.x) }}, {{ Math.round(field.rect.y) }}
                  &middot; {{ Math.round(field.rect.width) }}&times;{{ Math.round(field.rect.height) }}
                </template>
                <template v-else>—</template>
              </td>
              <td>{{ field.value ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="panel">
        <h3>Pages</h3>
        <table>
          <thead><tr><th>Page</th><th>Width (pt)</th><th>Height (pt)</th></tr></thead>
          <tbody>
            <tr v-for="page in info.pages" :key="page.page">
              <td>{{ page.page }}</td><td>{{ page.width }}</td><td>{{ page.height }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
