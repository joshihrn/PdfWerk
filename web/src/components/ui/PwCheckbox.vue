<script setup lang="ts">
/** A checkbox whose label is part of the hit target — the label is usually the larger one. */
import { useId } from 'vue'

withDefaults(
  defineProps<{
    label: string
    help?: string
    disabled?: boolean
  }>(),
  { disabled: false },
)

const model = defineModel<boolean>({ default: false })
const uid = useId()
</script>

<template>
  <div class="check">
    <input :id="`c-${uid}`" v-model="model" type="checkbox" class="check__box" :disabled="disabled" />

    <label :for="`c-${uid}`" class="check__label">
      <span>{{ label }}</span>
      <span v-if="help" class="check__help">{{ help }}</span>
    </label>
  </div>
</template>

<style scoped>
.check {
  display: flex;
  align-items: flex-start;
  gap: var(--s-2);
}

.check__box {
  width: 15px;
  height: 15px;
  margin: 2px 0 0;
  accent-color: var(--solid-bg);
  cursor: pointer;
  flex: none;
}

.check__box:disabled {
  cursor: not-allowed;
}

.check__label {
  font-size: var(--t-13);
  line-height: 1.45;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.check__box:disabled + .check__label {
  color: var(--fg-disabled);
  cursor: not-allowed;
}

.check__help {
  font-size: var(--t-12);
  color: var(--fg-subtle);
}
</style>
