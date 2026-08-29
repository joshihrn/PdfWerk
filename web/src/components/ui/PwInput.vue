<script setup lang="ts">
/** A text input. Pair with PwField; used bare only where a label would be redundant. */
withDefaults(
  defineProps<{
    id?: string
    type?: string
    placeholder?: string
    disabled?: boolean
    readonly?: boolean
    invalid?: boolean
    describedBy?: string
    mono?: boolean
    min?: number | string
    max?: number | string
    step?: number | string
    maxlength?: number
  }>(),
  { type: 'text', disabled: false, readonly: false, invalid: false, mono: false },
)

const model = defineModel<string | number>()
</script>

<template>
  <input
    :id="id"
    v-model="model"
    class="input"
    :class="{ 'input--mono': mono }"
    :type="type"
    :placeholder="placeholder"
    :disabled="disabled"
    :readonly="readonly"
    :aria-invalid="invalid || undefined"
    :aria-describedby="describedBy"
    :min="min"
    :max="max"
    :step="step"
    :maxlength="maxlength"
  />
</template>

<style scoped>
.input {
  width: 100%;
  height: var(--control-h);
  padding: 0 var(--s-3);
  background: var(--bg-field);
  color: var(--fg);
  border: 1px solid var(--border-field);
  border-radius: var(--r-md);
  font-family: inherit;
  font-size: var(--t-13);
  transition: border-color var(--fast) var(--ease), box-shadow var(--fast) var(--ease);
}

.input--mono {
  font-family: var(--mono);
}

.input::placeholder {
  color: var(--fg-disabled);
}

.input:hover:not(:disabled) {
  border-color: var(--border-strong);
}

.input:focus-visible {
  border-color: var(--focus);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--focus) 22%, transparent);
}

.input:disabled {
  background: var(--bg-disabled);
  color: var(--fg-disabled);
  cursor: not-allowed;
}

.input[aria-invalid='true'] {
  border-color: var(--bad-bd);
}

.input[aria-invalid='true']:focus-visible {
  border-color: var(--bad-fg);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--bad-fg) 20%, transparent);
}

/* Spinners are noise in a dense form, and easy to nudge by accident while scrolling. */
.input[type='number'] {
  appearance: textfield;
  -moz-appearance: textfield;
}

.input[type='number']::-webkit-outer-spin-button,
.input[type='number']::-webkit-inner-spin-button {
  appearance: none;
  margin: 0;
}
</style>
