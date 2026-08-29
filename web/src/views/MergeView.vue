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
  if (files.value.length < 2) return

  busy.value = true
  error.value = null

  try {
    const produced = await api.merge(files.value, delivery)
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
    <h1>Merge PDFs</h1>
    <p class="muted">
      Combine documents in the order you choose. Interactive form fields are carried across, so a
      merged pack can still be filled in afterwards.
    </p>

    <div class="split">
      <div class="panel" style="margin: 0">
        <FileDrop
          v-model="files"
          multiple
          label="Drop PDFs here, or click to choose"
          hint="Use the arrows to reorder before merging"
        />

        <div class="btns">
          <button class="btn primary" :disabled="busy || files.length < 2" @click="run('stream')">Preview</button>
          <button class="btn" :disabled="busy || files.length < 2" @click="run('download')">Download</button>
        </div>

        <p v-if="files.length === 1" class="small muted" style="margin-top: 10px">
          Add at least one more file to merge.
        </p>
      </div>

      <ResultPane :result="result" :error="error" :busy="busy" idle-hint="The combined document appears here." />
    </div>
  </div>
</template>
