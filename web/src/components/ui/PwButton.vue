<script setup lang="ts">
/**
 * The one button.
 *
 * Variants encode intent rather than colour, so a redesign changes tokens instead of every
 * call site. `solid` is near-black rather than accent-coloured: on a dense working screen the
 * highest-contrast element should be the action you most likely want, and reserving the accent
 * for links and focus keeps "blue" meaning "interactive" everywhere else.
 */
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    variant?: 'solid' | 'outline' | 'ghost' | 'danger'
    size?: 'sm' | 'md' | 'lg'
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    /** Shows a spinner, disables interaction, and keeps the button's width stable. */
    loading?: boolean
    /** Renders as a link while keeping button styling. */
    href?: string
    block?: boolean
  }>(),
  { variant: 'outline', size: 'md', type: 'button', disabled: false, loading: false, block: false },
)

const emit = defineEmits<{ click: [MouseEvent] }>()

const tag = computed(() => (props.href ? 'a' : 'button'))
const isDisabled = computed(() => props.disabled || props.loading)

function onClick(event: MouseEvent) {
  if (isDisabled.value) {
    event.preventDefault()
    return
  }

  emit('click', event)
}
</script>

<template>
  <component
    :is="tag"
    class="btn"
    :class="[`btn--${variant}`, `btn--${size}`, { 'btn--block': block, 'btn--loading': loading }]"
    :type="href ? undefined : type"
    :href="href"
    :disabled="href ? undefined : isDisabled"
    :aria-disabled="href && isDisabled ? 'true' : undefined"
    :aria-busy="loading ? 'true' : undefined"
    @click="onClick"
  >
    <!-- Kept in flow rather than replacing the label, so the button does not resize
         mid-interaction and shift everything next to it. -->
    <span v-if="loading" class="btn__spinner" aria-hidden="true"></span>
    <slot name="icon" />
    <span class="btn__label"><slot /></span>
  </component>
</template>

<style scoped>
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--s-2);
  height: var(--control-h);
  padding: 0 var(--s-3);
  border-radius: var(--r-md);
  border: 1px solid transparent;
  font-family: inherit;
  font-size: var(--t-13);
  font-weight: var(--w-medium);
  line-height: 1;
  white-space: nowrap;
  cursor: pointer;
  user-select: none;
  text-decoration: none;
  transition: background-color var(--fast) var(--ease), border-color var(--fast) var(--ease),
    color var(--fast) var(--ease);
}

.btn:hover { text-decoration: none; }

.btn:disabled,
.btn[aria-disabled='true'] {
  cursor: not-allowed;
  opacity: 0.5;
}

.btn--block { width: 100%; }

/* ---- sizes ---- */
.btn--sm { height: 26px; padding: 0 var(--s-2); font-size: var(--t-12); }
.btn--lg { height: var(--control-h-lg); padding: 0 var(--s-4); font-size: var(--t-14); }

/* ---- variants ---- */
.btn--solid {
  background: var(--solid-bg);
  color: var(--solid-fg);
  border-color: var(--solid-bg);
}
.btn--solid:hover:not(:disabled):not([aria-disabled='true']) {
  background: var(--solid-bg-hover);
  border-color: var(--solid-bg-hover);
}

.btn--outline {
  background: var(--bg-raised);
  color: var(--fg);
  border-color: var(--border-strong);
  box-shadow: var(--shadow-sm);
}
.btn--outline:hover:not(:disabled):not([aria-disabled='true']) { background: var(--bg-hover); }

.btn--ghost {
  background: transparent;
  color: var(--fg-muted);
}
.btn--ghost:hover:not(:disabled):not([aria-disabled='true']) {
  background: var(--bg-hover);
  color: var(--fg);
}

.btn--danger {
  background: var(--bg-raised);
  color: var(--bad-fg);
  border-color: var(--bad-bd);
  box-shadow: var(--shadow-sm);
}
.btn--danger:hover:not(:disabled):not([aria-disabled='true']) { background: var(--bad-bg); }

/* ---- spinner ---- */
.btn__spinner {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  border: 1.5px solid currentColor;
  border-top-color: transparent;
  opacity: 0.85;
  animation: btn-spin 620ms linear infinite;
  flex: none;
}

@keyframes btn-spin { to { transform: rotate(360deg); } }

.btn__label { display: inline-flex; align-items: center; }
</style>
