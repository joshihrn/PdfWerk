<script setup lang="ts">
/**
 * A bounded surface. Structure comes from a border rather than a shadow — stacked shadows read
 * as decoration, and on a screen with several panels they turn into visual noise.
 */
withDefaults(
  defineProps<{
    title?: string
    description?: string
    /** Removes body padding, for content that manages its own — tables, canvases, previews. */
    flush?: boolean
    as?: string
  }>(),
  { flush: false, as: 'section' },
)
</script>

<template>
  <component :is="as" class="card">
    <header v-if="title || $slots.header || $slots.actions" class="card__head">
      <div class="card__heading">
        <h3 v-if="title" class="card__title">{{ title }}</h3>
        <p v-if="description" class="card__desc">{{ description }}</p>
        <slot name="header" />
      </div>
      <div v-if="$slots.actions" class="card__actions"><slot name="actions" /></div>
    </header>

    <div class="card__body" :class="{ 'card__body--flush': flush }">
      <slot />
    </div>

    <footer v-if="$slots.footer" class="card__foot">
      <slot name="footer" />
    </footer>
  </component>
</template>

<style scoped>
.card {
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
  overflow: hidden;
}

.card__head {
  display: flex;
  align-items: flex-start;
  gap: var(--s-4);
  padding: var(--s-4) var(--s-5);
  border-bottom: 1px solid var(--border);
}

.card__heading {
  flex: 1 1 auto;
  min-width: 0;
}

.card__title {
  font-size: var(--t-14);
  font-weight: var(--w-semi);
  letter-spacing: var(--track-snug);
}

.card__desc {
  margin-top: 2px;
  font-size: var(--t-12);
  color: var(--fg-subtle);
  line-height: 1.45;
}

.card__actions {
  flex: none;
  display: flex;
  align-items: center;
  gap: var(--s-2);
}

.card__body {
  padding: var(--s-5);
}

.card__body--flush {
  padding: 0;
}

.card__foot {
  display: flex;
  align-items: center;
  gap: var(--s-2);
  padding: var(--s-3) var(--s-5);
  border-top: 1px solid var(--border);
  background: var(--bg-sunken);
}
</style>
