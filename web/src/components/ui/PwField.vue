<script setup lang="ts">
/**
 * Label, control, help text and error, wired together.
 *
 * Exists so accessibility is the default rather than something each form remembers: the id is
 * generated once and threaded to the control, help and error through aria-describedby. Forms
 * that hand-roll this get it right the first time and drift afterwards.
 */
import { computed, useId } from 'vue'

const props = withDefaults(
  defineProps<{
    label: string
    help?: string
    error?: string
    required?: boolean
    /** Hides the label visually but keeps it for assistive technology. */
    hideLabel?: boolean
  }>(),
  { required: false, hideLabel: false },
)

const uid = useId()
const controlId = computed(() => `f-${uid}`)
const helpId = computed(() => `f-${uid}-help`)
const errorId = computed(() => `f-${uid}-error`)

const describedBy = computed(
  () =>
    [props.help ? helpId.value : null, props.error ? errorId.value : null]
      .filter(Boolean)
      .join(' ') || undefined,
)
</script>

<template>
  <div class="field" :class="{ 'field--invalid': !!error }">
    <label :for="controlId" :class="['field__label', { 'sr-only': hideLabel }]">
      {{ label }}
      <span v-if="required" class="field__req" aria-hidden="true">*</span>
    </label>

    <slot :id="controlId" :described-by="describedBy" :invalid="!!error" />

    <p v-if="help && !error" :id="helpId" class="field__help">{{ help }}</p>
    <p v-if="error" :id="errorId" class="field__error">{{ error }}</p>
  </div>
</template>

<style scoped>
.field {
  display: flex;
  flex-direction: column;
  gap: var(--s-1);
  min-width: 0;
}

.field__label {
  font-size: var(--t-12);
  font-weight: var(--w-medium);
  color: var(--fg-muted);
  line-height: 1.4;
}

.field__req {
  color: var(--bad-fg);
  margin-left: 1px;
}

.field__help {
  font-size: var(--t-12);
  color: var(--fg-subtle);
  line-height: 1.4;
}

.field__error {
  font-size: var(--t-12);
  color: var(--bad-fg);
  line-height: 1.4;
}
</style>
