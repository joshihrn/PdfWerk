<script setup lang="ts">
import { ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import ResultPane from '../components/ResultPane.vue'
import { api, saveBlob, type Delivery, type DocumentResult } from '../api/client'

const files = ref<File[]>([])
const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

async function run(delivery: Delivery) {
  if (!files.value[0]) return

  busy.value = true
  error.value = null

  try {
    const produced = await api.createFromWord(files.value[0], delivery)
    result.value = produced
    if (delivery === 'download') saveBlob(produced.blob, produced.fileName)
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
    <h1>Word to PDF</h1>
    <p class="muted">
      Converts .docx with layout preserved. Where LibreOffice is installed it is used for full
      fidelity; otherwise a built-in renderer handles headings, formatting, lists, tables and images.
    </p>

    <div class="split">
      <div class="panel" style="margin: 0">
        <FileDrop
          v-model="files"
          accept=".docx,.doc,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
          label="Drop a Word document here, or click to choose"
          hint=".docx works everywhere; .doc needs a server with LibreOffice"
        />

        <div class="btns">
          <button class="btn primary" :disabled="busy || !files.length" @click="run('stream')">Preview</button>
          <button class="btn" :disabled="busy || !files.length" @click="run('download')">Download</button>
        </div>
      </div>

      <div class="stack">
        <ResultPane
          :result="result"
          :error="error"
          :busy="busy"
          idle-hint="The converted PDF appears here, tagged with which converter produced it."
        />

        <div class="panel small muted" style="margin: 0">
          <h3>Which converter ran?</h3>
          <p style="margin-bottom: 0">
            The result is labelled <span class="tag grey">via libreoffice</span> or
            <span class="tag grey">via openxml</span>. They differ in fidelity, so if a complex
            document comes out wrong, that label tells you whether a LibreOffice host would do better.
          </p>
        </div>
      </div>
    </div>
  </div>
</template>
