<script setup lang="ts">
import { computed, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import ResultPane from '../components/ResultPane.vue'
import { PwButton, PwCard, PwPageHeader } from '../components/ui'
import { api, saveBlob, type Delivery, type DocumentResult } from '../api/client'

const files = ref<File[]>([])
const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

const ready = computed(() => files.value.length > 0)

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
    <PwPageHeader
      title="Word to PDF"
      description="Converts .docx with layout preserved. Where LibreOffice is available it is used for full fidelity; otherwise a built-in renderer handles headings, formatting, lists, tables and images."
    />

    <div class="split">
      <PwCard title="Document">
        <FileDrop
          v-model="files"
          accept=".docx,.doc,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
          label="Drop a Word document here, or browse"
          hint=".docx works everywhere · .doc needs a server with LibreOffice"
        />

        <template #footer>
          <PwButton variant="solid" :loading="busy" :disabled="!ready" @click="run('stream')">
            Convert &amp; preview
          </PwButton>
          <PwButton :disabled="busy || !ready" @click="run('download')">Download</PwButton>
        </template>
      </PwCard>

      <div class="stack-4">
        <ResultPane
          :result="result"
          :error="error"
          :busy="busy"
          busy-hint="Converting the document…"
          idle-hint="The converted PDF appears here, tagged with which converter produced it."
        />

        <PwCard title="Which converter ran?">
          <p class="t-13 muted" style="margin: 0">
            The result is labelled <strong>via libreoffice</strong> or <strong>via openxml</strong>.
            They differ in fidelity, so if a complex document comes out wrong, that label tells you
            whether a host with LibreOffice would do better.
          </p>
        </PwCard>
      </div>
    </div>
  </div>
</template>
