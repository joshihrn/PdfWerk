<script setup lang="ts">
import { computed, ref } from 'vue'
import { formatBytes } from '../api/client'

const props = withDefaults(
  defineProps<{
    accept?: string
    multiple?: boolean
    label?: string
    hint?: string
  }>(),
  {
    accept: 'application/pdf',
    multiple: false,
    label: 'Drop a PDF here, or click to choose',
    hint: '',
  },
)

const files = defineModel<File[]>({ default: () => [] })

const over = ref(false)
const input = ref<HTMLInputElement>()

const summary = computed(() =>
  files.value.length === 0
    ? ''
    : `${files.value.length} file${files.value.length === 1 ? '' : 's'} · ${formatBytes(
        files.value.reduce((total, f) => total + f.size, 0),
      )}`,
)

function take(incoming: FileList | null) {
  if (!incoming || incoming.length === 0) return

  const next = Array.from(incoming)
  // Replacing rather than appending in single mode avoids the surprise of an old file being
  // submitted when the user thought they had swapped it.
  files.value = props.multiple ? [...files.value, ...next] : next.slice(0, 1)
}

function onDrop(event: DragEvent) {
  over.value = false
  take(event.dataTransfer?.files ?? null)
}

function remove(index: number) {
  files.value = files.value.filter((_, i) => i !== index)
}

function move(index: number, delta: number) {
  const target = index + delta
  if (target < 0 || target >= files.value.length) return

  const next = [...files.value]
  ;[next[index], next[target]] = [next[target], next[index]]
  files.value = next
}
</script>

<template>
  <div>
    <div
      class="drop"
      :class="{ over }"
      role="button"
      tabindex="0"
      @click="input?.click()"
      @keydown.enter.prevent="input?.click()"
      @keydown.space.prevent="input?.click()"
      @dragover.prevent="over = true"
      @dragleave.prevent="over = false"
      @drop.prevent="onDrop"
    >
      <div>{{ label }}</div>
      <div v-if="hint" class="small muted" style="margin-top: 6px">{{ hint }}</div>
      <div v-if="summary" class="small" style="margin-top: 8px; color: var(--accent-2)">{{ summary }}</div>

      <input
        ref="input"
        type="file"
        :accept="accept"
        :multiple="multiple"
        @change="take(($event.target as HTMLInputElement).files)"
      />
    </div>

    <div v-if="files.length" class="filelist">
      <div v-for="(file, index) in files" :key="`${file.name}-${index}`" class="fileitem">
        <!-- Order matters for merge, so it is adjustable rather than fixed by selection order. -->
        <template v-if="multiple && files.length > 1">
          <button class="btn small" :disabled="index === 0" title="Move up" @click="move(index, -1)">↑</button>
          <button class="btn small" :disabled="index === files.length - 1" title="Move down" @click="move(index, 1)">↓</button>
        </template>

        <span class="name">{{ file.name }}</span>
        <span class="size">{{ formatBytes(file.size) }}</span>
        <button class="btn small danger" title="Remove" @click="remove(index)">✕</button>
      </div>
    </div>
  </div>
</template>
