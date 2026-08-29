<script setup lang="ts">
/**
 * A native select.
 *
 * Native on purpose: it is keyboard-correct, screen-reader-correct and renders as a proper
 * picker on mobile, all for free. A custom listbox would have to re-earn every one of those,
 * and the only thing gained is control over the option list's appearance.
 */
withDefaults(
  defineProps<{
    id?: string
    options: { value: string | number; label: string; disabled?: boolean }[]
    disabled?: boolean
    invalid?: boolean
    describedBy?: string
  }>(),
  { disabled: false, invalid: false },
)

const model = defineModel<string | number>()
</script>

<template>
  <div class="select">
    <select
      :id="id"
      v-model="model"
      class="select__el"
      :disabled="disabled"
      :aria-invalid="invalid || undefined"
      :aria-describedby="describedBy"
    >
      <option v-for="option in options" :key="option.value" :value="option.value" :disabled="option.disabled">
        {{ option.label }}
      </option>
    </select>

    <svg class="select__chevron" viewBox="0 0 12 12" aria-hidden="true" focusable="false">
      <path
        d="M3 4.5 6 7.5 9 4.5"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
  </div>
</template>

<style scoped>
.select {
  position: relative;
  display: block;
}

.select__el {
  width: 100%;
  height: var(--control-h);
  padding: 0 var(--s-6) 0 var(--s-3);
  background: var(--bg-field);
  color: var(--fg);
  border: 1px solid var(--border-field);
  border-radius: var(--r-md);
  font-family: inherit;
  font-size: var(--t-13);
  appearance: none;
  cursor: pointer;
  transition: border-color var(--fast) var(--ease), box-shadow var(--fast) var(--ease);
}

.select__el:hover:not(:disabled) {
  border-color: var(--border-strong);
}

.select__el:focus-visible {
  border-color: var(--focus);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--focus) 22%, transparent);
}

.select__el:disabled {
  background: var(--bg-disabled);
  color: var(--fg-disabled);
  cursor: not-allowed;
}

.select__chevron {
  position: absolute;
  right: var(--s-2);
  top: 50%;
  width: 12px;
  height: 12px;
  margin-top: -6px;
  color: var(--fg-subtle);
  pointer-events: none;
}
</style>
