<script setup lang="ts">
/**
 * A small status label. Deliberately low-contrast: badges annotate, they do not compete
 * with the content they sit beside.
 */
withDefaults(
  defineProps<{
    tone?: 'neutral' | 'accent' | 'ok' | 'warn' | 'bad'
    /** Adds a filled dot, for status that is scanned down a column rather than read. */
    dot?: boolean
    mono?: boolean
  }>(),
  { tone: 'neutral', dot: false, mono: false },
)
</script>

<template>
  <span class="badge" :class="[`badge--${tone}`, { 'badge--mono': mono }]">
    <span v-if="dot" class="badge__dot" aria-hidden="true"></span>
    <slot />
  </span>
</template>

<style scoped>
.badge {
  display: inline-flex;
  align-items: center;
  gap: var(--s-1);
  height: 20px;
  padding: 0 var(--s-2);
  border-radius: var(--r-sm);
  border: 1px solid;
  font-size: var(--t-11);
  font-weight: var(--w-medium);
  line-height: 1;
  white-space: nowrap;
  vertical-align: middle;
}

.badge--mono { font-family: var(--mono); font-size: var(--t-11); }

.badge__dot { width: 5px; height: 5px; border-radius: 50%; background: currentColor; flex: none; }

.badge--neutral { background: var(--bg-sunken); border-color: var(--border); color: var(--fg-muted); }
.badge--accent  { background: var(--a-50);      border-color: var(--a-200);  color: var(--a-700); }
.badge--ok      { background: var(--ok-bg);     border-color: var(--ok-bd);  color: var(--ok-fg); }
.badge--warn    { background: var(--warn-bg);   border-color: var(--warn-bd);color: var(--warn-fg); }
.badge--bad     { background: var(--bad-bg);    border-color: var(--bad-bd); color: var(--bad-fg); }

:root[data-theme='dark'] .badge--accent { background: #16203a; border-color: #2a3a63; color: #9db4f5; }
</style>
