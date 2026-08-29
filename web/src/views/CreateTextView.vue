<script setup lang="ts">
import { ref } from 'vue'
import ResultPane from '../components/ResultPane.vue'
import { api, type Delivery, type DocumentResult } from '../api/client'

const content = ref(`# Service Agreement

This agreement is made between **Acme Corporation** and the client.

## Terms

1. Payment is due within 30 days of invoice.
2. Either party may terminate with 60 days written notice.
3. All work product remains the property of the client.

| Item | Amount |
| --- | ---: |
| Setup | 1,200 |
| Monthly retainer | 350 |

> Signed on behalf of both parties.

See the [full terms](https://example.com/terms) for details.
`)

const title = ref('Service Agreement')
const author = ref('')
const format = ref<'Markdown' | 'Plain'>('Markdown')
const page = ref('A4')
const orientation = ref('Portrait')
const marginMm = ref(20)
const fontSize = ref(11)
const pageNumbers = ref(true)

const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

async function run(delivery: Delivery) {
  busy.value = true
  error.value = null

  try {
    const produced = await api.createFromText(
      {
        content: content.value,
        title: title.value || null,
        author: author.value || null,
        format: format.value,
        page: page.value,
        orientation: orientation.value,
        marginMm: marginMm.value,
        fontSize: fontSize.value,
        pageNumbers: pageNumbers.value,
      },
      delivery,
    )

    result.value = produced
    if (delivery === 'download') {
      const { saveBlob } = await import('../api/client')
      saveBlob(produced.blob, produced.fileName)
    }
  } catch (ex) {
    result.value = null
    error.value = ex
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div>
    <h1>Create a PDF from text</h1>
    <p class="muted">
      Write Markdown or plain text and get a clean, paginated document. Headings, lists, tables,
      block quotes, code and links are all rendered.
    </p>

    <div class="split">
      <div class="panel" style="margin: 0">
        <label for="content">Content</label>
        <textarea id="content" v-model="content" style="min-height: 340px"></textarea>

        <div class="row" style="margin-top: 14px">
          <div>
            <label for="title">Title</label>
            <input id="title" v-model="title" type="text" placeholder="Used for the heading and file name" />
          </div>
          <div>
            <label for="author">Author</label>
            <input id="author" v-model="author" type="text" placeholder="Optional byline" />
          </div>
        </div>

        <div class="btns">
          <button class="btn primary" :disabled="busy || !content.trim()" @click="run('stream')">Preview</button>
          <button class="btn" :disabled="busy || !content.trim()" @click="run('download')">Download</button>
        </div>
      </div>

      <div class="stack">
        <div class="panel" style="margin: 0">
          <h3>Layout</h3>

          <label for="format">Format</label>
          <select id="format" v-model="format">
            <option>Markdown</option>
            <option>Plain</option>
          </select>

          <label for="page" style="margin-top: 10px">Page size</label>
          <select id="page" v-model="page">
            <option>A4</option><option>Letter</option><option>Legal</option>
            <option>A3</option><option>A5</option>
          </select>

          <label for="orientation" style="margin-top: 10px">Orientation</label>
          <select id="orientation" v-model="orientation">
            <option>Portrait</option><option>Landscape</option>
          </select>

          <div class="row" style="margin-top: 10px">
            <div>
              <label for="margin">Margin (mm)</label>
              <input id="margin" v-model.number="marginMm" type="number" min="0" max="100" />
            </div>
            <div>
              <label for="fontsize">Font size</label>
              <input id="fontsize" v-model.number="fontSize" type="number" min="5" max="48" />
            </div>
          </div>

          <label style="margin-top: 12px; display: flex; align-items: center; gap: 8px; cursor: pointer">
            <input v-model="pageNumbers" type="checkbox" style="width: auto" />
            <span>Show "Page N of M" footer</span>
          </label>
        </div>

        <ResultPane
          :result="result"
          :error="error"
          :busy="busy"
          idle-hint="Preview renders the PDF here without downloading it."
        />
      </div>
    </div>
  </div>
</template>
