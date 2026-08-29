<script setup lang="ts">
/**
 * File selection by click, keyboard or drop.
 *
 * The drop zone shrinks to a compact strip once files are chosen: at that point the list is the
 * useful thing on screen, and a large empty target competing with it is wasted space.
 */
import { computed, ref } from 'vue'
import { formatBytes } from '../api/client'
import { PwButton } from './ui'

const props = withDefaults(
  defineProps<{
    accept?: string
    multiple?: boolean
    label?: string
    hint?: string
    /** Lets the caller reorder — merge cares about sequence, nothing else does. */
    reorderable?: boolean
  }>(),
  {
    accept: 'application/pdf',
    multiple: false,
    label: 'Drop a PDF here, or browse',
    hint: '',
    reorderable: false,
  },
)

const files = defineModel<File[]>({ default: () => [] })

const over = ref(false)
const input = ref<HTMLInputElement>()

const totalSize = computed(() => files.value.reduce((sum, f) => sum + f.size, 0))

function take(incoming: FileList | null) {
  if (!incoming?.length) return

  const next = Array.from(incoming)

  // Single mode replaces rather than appends: otherwise swapping a file silently submits the old.
  files.value = props.multiple ? [...files.value, ...next] : next.slice(0, 1)

  // Cleared so choosing the same file twice in a row still fires a change event.
  if (input.value) input.value.value = ''
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
  <div class="drop-wrap">
    <div
      class="drop"
      :class="{ 'drop--over': over, 'drop--compact': files.length > 0 }"
      role="button"
      tabindex="0"
      :aria-label="label"
      @click="input?.click()"
      @keydown.enter.prevent="input?.click()"
      @keydown.space.prevent="input?.click()"
      @dragover.prevent="over = true"
      @dragenter.prevent="over = true"
      @dragleave.prevent="over = false"
      @drop.prevent="onDrop"
    >
      <svg class="drop__icon" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
        <path
          d="M8 10.5V2.5m0 0L5 5.5m3-3 3 3M2.5 10v2.5a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1V10"
          fill="none"
          stroke="currentColor"
          stroke-width="1.3"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>

      <span class="drop__label">{{ files.length ? 'Add or replace' : label }}</span>
      <span v-if="hint && !files.length" class="drop__hint">{{ hint }}</span>

      <input
        ref="input"
        type="file"
        class="drop__input"
        :accept="accept"
        :multiple="multiple"
        @change="take(($event.target as HTMLInputElement).files)"
      />
    </div>

    <ul v-if="files.length" class="files">
      <li v-for="(file, index) in files" :key="`${file.name}-${index}`" class="file">
        <template v-if="reorderable && files.length > 1">
          <div class="file__order">
            <button type="button" class="file__nudge" :disabled="index === 0"
                    :aria-label="`Move ${file.name} earlier`" @click="move(index, -1)">↑</button>
            <button type="button" class="file__nudge" :disabled="index === files.length - 1"
                    :aria-label="`Move ${file.name} later`" @click="move(index, 1)">↓</button>
          </div>
          <span class="file__index">{{ index + 1 }}</span>
        </template>

        <span class="file__name truncate" :title="file.name">{{ file.name }}</span>
        <span class="file__size">{{ formatBytes(file.size) }}</span>

        <PwButton variant="ghost" size="sm" :aria-label="`Remove ${file.name}`" @click="remove(index)">
          Remove
        </PwButton>
      </li>
    </ul>

    <p v-if="files.length > 1" class="files__total">
      {{ files.length }} files · {{ formatBytes(totalSize) }}
    </p>
  </div>
</template>

<style scoped>
.drop-wrap {
  display: flex;
  flex-direction: column;
  gap: var(--s-3);
}

.drop {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--s-2);
  padding: var(--s-8) var(--s-4);
  background: var(--bg-sunken);
  border: 1px dashed var(--border-strong);
  border-radius: var(--r-md);
  color: var(--fg-muted);
  font-size: var(--t-13);
  cursor: pointer;
  transition: border-color var(--fast) var(--ease), background-color var(--fast) var(--ease),
    color var(--fast) var(--ease), padding var(--base) var(--ease);
}

.drop:hover,
.drop--over {
  border-color: var(--focus);
  background: var(--bg-hover);
  color: var(--fg);
}

.drop--compact {
  flex-direction: row;
  padding: var(--s-3) var(--s-4);
}

.drop__icon {
  width: 16px;
  height: 16px;
  flex: none;
  opacity: 0.75;
}

.drop__label {
  font-weight: var(--w-medium);
}

.drop__hint {
  font-size: var(--t-12);
  color: var(--fg-subtle);
}

.drop__input {
  display: none;
}

.files {
  list-style: none;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--s-1);
}

.file {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  padding: var(--s-2) var(--s-2) var(--s-2) var(--s-3);
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  font-size: var(--t-13);
}

.file__order {
  display: flex;
  flex-direction: column;
  gap: 1px;
  flex: none;
}

.file__nudge {
  width: 18px;
  height: 13px;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: 2px;
  background: var(--bg-sunken);
  color: var(--fg-muted);
  font-size: 9px;
  line-height: 1;
  cursor: pointer;
}

.file__nudge:hover:not(:disabled) {
  background: var(--bg-active);
  color: var(--fg);
}

.file__nudge:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.file__index {
  flex: none;
  width: 16px;
  font-size: var(--t-11);
  color: var(--fg-subtle);
  text-align: center;
}

.file__name {
  flex: 1 1 auto;
  min-width: 0;
}

.file__size {
  flex: none;
  font-size: var(--t-12);
  color: var(--fg-subtle);
  font-variant-numeric: tabular-nums;
}

.files__total {
  font-size: var(--t-12);
  color: var(--fg-subtle);
}
</style>
