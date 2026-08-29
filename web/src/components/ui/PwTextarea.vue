<script setup lang="ts">
/** A multi-line input. Monospaced by default, since most of its uses here hold Markdown or JSON. */
withDefaults(
  defineProps<{
    id?: string
    placeholder?: string
    disabled?: boolean
    invalid?: boolean
    describedBy?: string
    rows?: number
    mono?: boolean
  }>(),
  { disabled: false, invalid: false, rows: 8, mono: true },
)

const model = defineModel<string>()
</script>

<template>
  <textarea
    :id="id"
    v-model="model"
    class="textarea"
    :class="{ 'textarea--mono': mono }"
    :placeholder="placeholder"
    :disabled="disabled"
    :rows="rows"
    :aria-invalid="invalid || undefined"
    :aria-describedby="describedBy"
  ></textarea>
</template>

<style scoped>
.textarea {
  width: 100%;
  padding: var(--s-2) var(--s-3);
  background: var(--bg-field);
  color: var(--fg);
  border: 1px solid var(--border-field);
  border-radius: var(--r-md);
  font-family: inherit;
  font-size: var(--t-13);
  line-height: 1.6;
  resize: vertical;
  min-height: 96px;
  box-shadow: var(--inset);
  transition: border-color var(--fast) var(--ease), box-shadow var(--fast) var(--ease);
}

.textarea--mono {
  font-family: var(--mono);
  font-size: var(--t-12);
}

.textarea::placeholder {
  color: var(--fg-disabled);
}

.textarea:hover:not(:disabled) {
  border-color: var(--border-strong);
}

.textarea:focus-visible {
  border-color: var(--focus);
  box-shadow: var(--inset), 0 0 0 3px color-mix(in srgb, var(--focus) 20%, transparent);
}

.textarea:disabled {
  background: var(--bg-disabled);
  color: var(--fg-disabled);
  cursor: not-allowed;
}

.textarea[aria-invalid='true'] {
  border-color: var(--bad-bd);
}
</style>
