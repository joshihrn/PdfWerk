<script setup lang="ts">
/**
 * A segmented control for switching between a few mutually exclusive modes.
 *
 * Built as a real tablist with roving tabindex and arrow-key navigation, because that is what
 * screen readers and keyboard users expect from something that looks like tabs. A row of
 * buttons styled to look like tabs behaves nothing like them.
 */
import { ref } from 'vue'

defineProps<{
  options: { value: string; label: string; badge?: string }[]
  label: string
}>()

const model = defineModel<string>({ required: true })
const tabs = ref<HTMLButtonElement[]>([])

function onKeydown(event: KeyboardEvent, index: number, options: { value: string }[]) {
  const last = options.length - 1

  const next =
    event.key === 'ArrowRight' || event.key === 'ArrowDown'
      ? index === last ? 0 : index + 1
      : event.key === 'ArrowLeft' || event.key === 'ArrowUp'
        ? index === 0 ? last : index - 1
        : event.key === 'Home'
          ? 0
          : event.key === 'End'
            ? last
            : null

  if (next === null) return

  event.preventDefault()
  model.value = options[next].value

  // Focus follows selection, which is the expected behaviour for an automatic tablist.
  tabs.value[next]?.focus()
}
</script>

<template>
  <div class="seg" role="tablist" :aria-label="label">
    <button
      v-for="(option, index) in options"
      :key="option.value"
      ref="tabs"
      type="button"
      role="tab"
      class="seg__item"
      :class="{ 'seg__item--on': model === option.value }"
      :aria-selected="model === option.value"
      :tabindex="model === option.value ? 0 : -1"
      @click="model = option.value"
      @keydown="onKeydown($event, index, options)"
    >
      {{ option.label }}
      <span v-if="option.badge" class="seg__badge">{{ option.badge }}</span>
    </button>
  </div>
</template>

<style scoped>
.seg {
  display: inline-flex;
  padding: 2px;
  background: var(--bg-sunken);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  gap: 2px;
}

.seg__item {
  display: inline-flex;
  align-items: center;
  gap: var(--s-2);
  height: 26px;
  padding: 0 var(--s-3);
  border: 0;
  border-radius: var(--r-sm);
  background: transparent;
  color: var(--fg-muted);
  font-family: inherit;
  font-size: var(--t-12);
  font-weight: var(--w-medium);
  cursor: pointer;
  white-space: nowrap;
  transition: background-color var(--fast) var(--ease), color var(--fast) var(--ease);
}

.seg__item:hover:not(.seg__item--on) {
  color: var(--fg);
}

.seg__item--on {
  background: var(--bg-raised);
  color: var(--fg);
  box-shadow: var(--shadow-sm);
}

.seg__badge {
  font-size: var(--t-11);
  color: var(--fg-subtle);
  font-variant-numeric: tabular-nums;
}
</style>
