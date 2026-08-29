<script setup lang="ts">
/**
 * The one button.
 *
 * Depth comes from three stacked cues rather than a gradient: a fine border, a one-pixel light
 * edge along the top, and a tight contact shadow. That combination is what separates a control
 * that looks moulded from one that looks drawn on — and it survives on any background, which a
 * gradient does not.
 *
 * It also moves. Press translates it down a pixel and pulls the shadow in, so the click is felt
 * rather than merely registered. Cheap to implement, and its absence is most of what makes an
 * interface feel dead.
 */
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    variant?: 'solid' | 'outline' | 'ghost' | 'danger' | 'accent'
    size?: 'sm' | 'md' | 'lg'
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    /** Shows a spinner, blocks interaction, and keeps the width stable. */
    loading?: boolean
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
    :class="[`btn--${variant}`, `btn--${size}`, { 'btn--block': block, 'is-loading': loading }]"
    :type="href ? undefined : type"
    :href="href"
    :disabled="href ? undefined : isDisabled"
    :aria-disabled="href && isDisabled ? 'true' : undefined"
    :aria-busy="loading ? 'true' : undefined"
    @click="onClick"
  >
    <span v-if="loading" class="btn__spinner" aria-hidden="true"></span>
    <slot name="icon" />
    <span class="btn__label"><slot /></span>
  </component>
</template>

<style scoped>
.btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--s-2);
  height: var(--control-h);
  padding: 0 14px;
  border-radius: var(--r-md);
  border: 1px solid transparent;
  font-family: inherit;
  font-size: var(--t-13);
  font-weight: var(--w-medium);
  letter-spacing: -0.005em;
  line-height: 1;
  white-space: nowrap;
  cursor: pointer;
  user-select: none;
  text-decoration: none;
  transition:
    background-color var(--fast) var(--ease),
    border-color var(--fast) var(--ease),
    color var(--fast) var(--ease),
    box-shadow var(--fast) var(--ease),
    transform var(--fast) var(--ease);
}

.btn:hover { text-decoration: none; }

/* The press. Translating the whole control and tightening its shadow reads as the button
   physically going down, which a colour change alone never achieves. */
.btn:active:not(:disabled):not([aria-disabled='true']) {
  transform: translateY(1px);
}

.btn:disabled,
.btn[aria-disabled='true'] {
  cursor: not-allowed;
  opacity: 0.45;
  box-shadow: none;
}

.btn--block { width: 100%; }

/* ---- sizes ---- */
.btn--sm {
  height: 28px;
  padding: 0 10px;
  font-size: var(--t-12);
  border-radius: var(--r-sm);
}

.btn--lg {
  height: var(--control-h-lg);
  padding: 0 18px;
  font-size: var(--t-14);
  border-radius: var(--r-lg);
}

/* ---- solid ----
   Near-black, with a faint interior top edge so it reads as a formed object. */
.btn--solid {
  background: var(--solid-bg);
  color: var(--solid-fg);
  border-color: var(--solid-bg);
  box-shadow: var(--highlight-solid), var(--shadow-sm);
}

.btn--solid:hover:not(:disabled):not([aria-disabled='true']) {
  background: var(--solid-bg-hover);
  border-color: var(--solid-bg-hover);
  box-shadow: var(--highlight-solid), var(--shadow-md);
}

.btn--solid:active:not(:disabled):not([aria-disabled='true']) {
  box-shadow: var(--highlight-solid), var(--shadow-xs);
}

/* ---- accent ---- */
.btn--accent {
  background: var(--a-600);
  color: #fff;
  border-color: var(--a-700);
  box-shadow: var(--highlight-solid), var(--shadow-sm);
}

.btn--accent:hover:not(:disabled):not([aria-disabled='true']) {
  background: var(--a-500);
  box-shadow: var(--highlight-solid), var(--shadow-md);
}

.btn--accent:active:not(:disabled):not([aria-disabled='true']) {
  box-shadow: var(--highlight-solid), var(--shadow-xs);
}

/* ---- outline ----
   The default. Light top edge, slightly stronger bottom border, tight contact shadow. */
.btn--outline {
  background: var(--bg-raised);
  color: var(--fg);
  border-color: var(--border-strong);
  border-bottom-color: color-mix(in srgb, var(--border-strong) 78%, var(--fg-subtle));
  box-shadow: var(--highlight), var(--shadow-xs);
}

.btn--outline:hover:not(:disabled):not([aria-disabled='true']) {
  background: var(--bg-hover);
  box-shadow: var(--highlight), var(--shadow-sm);
}

.btn--outline:active:not(:disabled):not([aria-disabled='true']) {
  background: var(--bg-active);
  box-shadow: inset 0 1px 2px rgba(16, 20, 28, 0.08);
}

/* ---- ghost ----
   No chrome at rest. For tertiary actions that should not compete. */
.btn--ghost {
  background: transparent;
  color: var(--fg-muted);
}

.btn--ghost:hover:not(:disabled):not([aria-disabled='true']) {
  background: var(--bg-hover);
  color: var(--fg);
}

.btn--ghost:active:not(:disabled):not([aria-disabled='true']) {
  background: var(--bg-active);
}

/* ---- danger ---- */
.btn--danger {
  background: var(--bg-raised);
  color: var(--bad-fg);
  border-color: var(--bad-bd);
  box-shadow: var(--highlight), var(--shadow-xs);
}

.btn--danger:hover:not(:disabled):not([aria-disabled='true']) {
  background: var(--bad-bg);
  box-shadow: var(--highlight), var(--shadow-sm);
}

.btn--danger:active:not(:disabled):not([aria-disabled='true']) {
  box-shadow: inset 0 1px 2px rgba(16, 20, 28, 0.08);
}

/* ---- loading ---- */
.btn__spinner {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  border: 1.5px solid currentColor;
  border-top-color: transparent;
  opacity: 0.8;
  animation: btn-spin 600ms linear infinite;
  flex: none;
}

@keyframes btn-spin { to { transform: rotate(360deg); } }

.btn__label {
  display: inline-flex;
  align-items: center;
}

/* On a dark ground the bottom-edge trick inverts: the darker edge belongs on top. */
:root[data-theme='dark'] .btn--outline {
  border-bottom-color: var(--border-strong);
  border-top-color: color-mix(in srgb, var(--border-strong) 70%, var(--fg-subtle));
}
</style>
