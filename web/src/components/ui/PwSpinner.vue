<script setup lang="ts">
/** A busy indicator. Always paired with text — a lone spinner says nothing about what is happening. */
withDefaults(defineProps<{ size?: number; label?: string }>(), { size: 14, label: 'Loading' })
</script>

<template>
  <span class="spin" role="status" :aria-label="label"
        :style="{ width: `${size}px`, height: `${size}px`, borderWidth: `${Math.max(1.5, size / 9)}px` }"></span>
</template>

<style scoped>
.spin {
  display: inline-block;
  border-style: solid;
  border-color: var(--border-strong);
  border-top-color: var(--fg-muted);
  border-radius: 50%;
  animation: spin 620ms linear infinite;
  flex: none;
}
@keyframes spin { to { transform: rotate(360deg); } }
</style>
