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

const ready = computed(() => files.value.length >= 2)

async function run(delivery: Delivery) {
  if (!ready.value) return

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
    <PwPageHeader
      title="Merge PDFs"
      description="Combine documents in the order you choose. Interactive form fields are carried across, so a merged pack can still be filled in afterwards."
    />

    <div class="split">
      <PwCard title="Documents" description="Use the arrows to set the order before merging.">
        <FileDrop
          v-model="files"
          multiple
          reorderable
          label="Drop PDFs here, or browse"
        />

        <template #footer>
          <PwButton variant="solid" :loading="busy" :disabled="!ready" @click="run('stream')">
            Merge &amp; preview
          </PwButton>
          <PwButton :disabled="busy || !ready" @click="run('download')">Download</PwButton>
          <span v-if="files.length === 1" class="t-12 subtle right">Add at least one more file</span>
        </template>
      </PwCard>

      <ResultPane
        :result="result"
        :error="error"
        :busy="busy"
        busy-hint="Merging…"
        idle-hint="The combined document appears here."
      />
    </div>
  </div>
</template>
