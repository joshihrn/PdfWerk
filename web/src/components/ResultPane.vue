<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ApiError, formatBytes, saveBlob, type DocumentResult } from '../api/client'

const props = defineProps<{
  result: DocumentResult | null
  error: unknown
  busy: boolean
  /** Shown while idle, so an untouched panel still explains itself. */
  idleHint?: string
}>()

const previewUrl = ref<string | null>(null)

// Object URLs leak if they are not revoked, and this pane is re-run on every submission.
watch(
  () => props.result,
  (result, _previous, onCleanup) => {
    previewUrl.value = result ? URL.createObjectURL(result.blob) : null

    const url = previewUrl.value
    onCleanup(() => {
      if (url) URL.revokeObjectURL(url)
    })
  },
  { immediate: true },
)

const message = computed(() => {
  if (!props.error) return null

  if (props.error instanceof ApiError) {
    return props.error.isRateLimit && props.error.retryAfterSeconds
      ? `${props.error.message}`
      : props.error.message
  }

  return props.error instanceof Error ? props.error.message : String(props.error)
})

const kind = computed(() => (props.error instanceof ApiError && props.error.isRateLimit ? 'warn' : 'err'))

function download() {
  if (props.result) saveBlob(props.result.blob, props.result.fileName)
}
</script>

<template>
  <div>
    <div v-if="busy" class="note info" style="display: flex; align-items: center; gap: 9px">
      <span class="spinner"></span> Working…
    </div>

    <div v-else-if="message" class="note" :class="kind">{{ message }}</div>

    <template v-else-if="result">
      <div class="note ok" style="display: flex; align-items: center; gap: 10px; flex-wrap: wrap">
        <span>{{ result.fileName }} · {{ formatBytes(result.blob.size) }}</span>
        <span v-if="result.converter" class="tag grey">via {{ result.converter }}</span>
        <span
          v-if="result.quota.remaining !== null"
          class="tag grey"
          :title="`Requests left in the current ${result.quota.window ?? 'window'}`"
        >
          {{ result.quota.remaining }}/{{ result.quota.limit }} left
        </span>
        <button class="btn small" style="margin-left: auto" @click="download">Download</button>
      </div>

      <iframe v-if="previewUrl" class="preview" :src="previewUrl" title="Result preview"></iframe>
    </template>

    <div v-else-if="idleHint" class="note info">{{ idleHint }}</div>
  </div>
</template>
