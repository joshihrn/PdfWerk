<script setup lang="ts">
/**
 * An inline message about the current screen — a result, a warning, a failure.
 *
 * Uses role="status" rather than "alert" by default: alert interrupts a screen reader
 * mid-sentence, which is right for an error and rude for "your PDF is ready".
 */
withDefaults(
  defineProps<{
    tone?: 'info' | 'ok' | 'warn' | 'bad'
    title?: string
    /** Errors should interrupt; everything else should wait its turn. */
    assertive?: boolean
  }>(),
  { tone: 'info', assertive: false },
)
</script>

<template>
  <div
    class="callout"
    :class="`callout--${tone}`"
    :role="assertive || tone === 'bad' ? 'alert' : 'status'"
    :aria-live="assertive || tone === 'bad' ? 'assertive' : 'polite'"
  >
    <div class="callout__body">
      <p v-if="title" class="callout__title">{{ title }}</p>
      <div class="callout__text"><slot /></div>
    </div>
    <div v-if="$slots.actions" class="callout__actions"><slot name="actions" /></div>
  </div>
</template>

<style scoped>
.callout {
  display: flex;
  align-items: flex-start;
  gap: var(--s-3);
  padding: var(--s-3) var(--s-4);
  border: 1px solid;
  border-radius: var(--r-md);
  font-size: var(--t-13);
}

.callout__body { flex: 1 1 auto; min-width: 0; }
.callout__title { font-weight: var(--w-semi); margin-bottom: 2px; }
.callout__text { line-height: var(--lh-snug); }
.callout__actions { flex: none; display: flex; gap: var(--s-2); align-items: center; }

.callout--info { background: var(--bg-sunken); border-color: var(--border);   color: var(--fg-muted); }
.callout--ok   { background: var(--ok-bg);     border-color: var(--ok-bd);    color: var(--ok-fg); }
.callout--warn { background: var(--warn-bg);   border-color: var(--warn-bd);  color: var(--warn-fg); }
.callout--bad  { background: var(--bad-bg);    border-color: var(--bad-bd);   color: var(--bad-fg); }
</style>
