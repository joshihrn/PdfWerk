<script setup lang="ts">
/**
 * The outcome of an operation: busy, failed, or a document with a preview.
 *
 * Quota is surfaced here rather than hidden, because on a rate-limited service the number of
 * calls you have left is part of the result — finding out by being refused is worse.
 */
import { computed, ref, watch } from 'vue'
import { ApiError, formatBytes, saveBlob, type DocumentResult } from '../api/client'
import { PwBadge, PwButton, PwCallout, PwSpinner } from './ui'

const props = defineProps<{
  result: DocumentResult | null
  error: unknown
  busy: boolean
  /** Shown before anything has run, so an untouched pane still explains itself. */
  idleHint?: string
  busyHint?: string
}>()

const previewUrl = ref<string | null>(null)

// Object URLs leak unless revoked, and this pane re-runs on every submission.
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
  if (props.error instanceof ApiError) return props.error.message
  return props.error instanceof Error ? props.error.message : String(props.error)
})

const isRateLimit = computed(() => props.error instanceof ApiError && props.error.isRateLimit)

/** A zip has nothing to preview; offering an empty frame would just look broken. */
const previewable = computed(() => props.result?.blob.type === 'application/pdf')

function download() {
  if (props.result) saveBlob(props.result.blob, props.result.fileName)
}
</script>

<template>
  <div class="result">
    <PwCallout v-if="busy" tone="info">
      <span class="row"><PwSpinner :size="13" /> {{ busyHint ?? 'Working…' }}</span>
    </PwCallout>

    <PwCallout
      v-else-if="message"
      :tone="isRateLimit ? 'warn' : 'bad'"
      :title="isRateLimit ? 'Rate limit reached' : 'That did not work'"
    >
      {{ message }}
    </PwCallout>

    <template v-else-if="result">
      <div class="result__bar">
        <div class="result__meta">
          <span class="result__name truncate" :title="result.fileName">{{ result.fileName }}</span>
          <span class="result__size">{{ formatBytes(result.blob.size) }}</span>

          <PwBadge v-if="result.converter" tone="neutral" mono>via {{ result.converter }}</PwBadge>

          <PwBadge
            v-if="result.quota.remaining !== null"
            :tone="result.quota.remaining <= 1 ? 'warn' : 'neutral'"
            :title="`Requests left in the current ${result.quota.window ?? 'window'}`"
          >
            {{ result.quota.remaining }}/{{ result.quota.limit }} left
          </PwBadge>
        </div>

        <PwButton variant="solid" size="sm" @click="download">Download</PwButton>
      </div>

      <iframe
        v-if="previewable && previewUrl"
        class="result__preview"
        :src="previewUrl"
        title="Result preview"
      ></iframe>
    </template>

    <PwCallout v-else-if="idleHint" tone="info">{{ idleHint }}</PwCallout>
  </div>
</template>

<style scoped>
.result {
  display: flex;
  flex-direction: column;
  gap: var(--s-3);
  min-width: 0;
}

.result__bar {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  padding: var(--s-2) var(--s-2) var(--s-2) var(--s-3);
  background: var(--ok-bg);
  border: 1px solid var(--ok-bd);
  border-radius: var(--r-md);
}

.result__meta {
  display: flex;
  align-items: center;
  gap: var(--s-2);
  flex-wrap: wrap;
  flex: 1 1 auto;
  min-width: 0;
  font-size: var(--t-13);
}

.result__name {
  font-weight: var(--w-medium);
  color: var(--ok-fg);
}

.result__size {
  font-size: var(--t-12);
  color: var(--ok-fg);
  opacity: 0.8;
  font-variant-numeric: tabular-nums;
}

.result__preview {
  width: 100%;
  height: 520px;
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: #fff;
}
</style>
