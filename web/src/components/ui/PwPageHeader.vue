<script setup lang="ts">
/** The heading block every tool page opens with, so they stay visually consistent. */
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <header class="ph">
    <div class="ph__text">
      <h1 class="ph__title">{{ title }}</h1>
      <p v-if="description" class="ph__desc">{{ description }}</p>
      <slot name="meta" />
    </div>
    <div v-if="$slots.actions" class="ph__actions"><slot name="actions" /></div>
  </header>
</template>

<style scoped>
.ph {
  display: flex;
  align-items: flex-start;
  gap: var(--s-4);
  padding-bottom: var(--s-5);
  margin-bottom: var(--s-5);
  border-bottom: 1px solid var(--border);
}

.ph__text {
  flex: 1 1 auto;
  min-width: 0;
}

.ph__title {
  font-size: var(--t-20);
  letter-spacing: var(--track-tight);
}

.ph__desc {
  margin-top: var(--s-1);
  font-size: var(--t-13);
  color: var(--fg-muted);
  line-height: var(--lh-snug);
  max-width: 68ch;
}

.ph__actions {
  flex: none;
  display: flex;
  align-items: center;
  gap: var(--s-2);
}
</style>
