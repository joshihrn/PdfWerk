<script setup lang="ts">
/**
 * Asks before any analytics load.
 *
 * Deliberately not a wall. It sits at the foot of the page, does not block the interface, and
 * gives decline the same weight as accept — a banner where refusing is a grey link buried under a
 * bright green "I agree" is not a choice, and a consent that was not freely given is not consent.
 *
 * It renders nothing at all when no measurement ID is configured, which is the normal state for a
 * self-hosted copy.
 */
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { PwButton } from './ui'
import { analyticsAvailable, decline, enable, getDecision } from '../analytics'

const visible = ref(false)
const bar = ref<HTMLElement | null>(null)

/**
 * Pads the page by the banner's height while it is up.
 *
 * The banner is fixed to the bottom so it is seen without scrolling, which means it sits on top
 * of whatever is down there — and what is down there is the footer, including the privacy link
 * this very banner points at. Reserving the space is the difference between a notice and an
 * obstruction.
 */
async function reserveSpace() {
  await nextTick()

  document.body.style.paddingBottom = visible.value && bar.value
    ? `${bar.value.offsetHeight}px`
    : ''
}

watch(visible, reserveSpace)
onBeforeUnmount(() => { document.body.style.paddingBottom = '' })

function accept() {
  enable()
  visible.value = false
}

function refuse() {
  decline()
  visible.value = false
}

onMounted(async () => {
  // Nothing to consent to if the operator has not configured a property.
  visible.value = analyticsAvailable() && getDecision() === null
  await reserveSpace()
})

defineExpose({ open: () => (visible.value = true) })
</script>

<template>
  <!--
    role="dialog" rather than "alert": it is a choice to be made, not an emergency, and it must
    not interrupt a screen reader mid-sentence. aria-live is deliberately absent for the same
    reason.
  -->
  <div
    v-if="visible"
    ref="bar"
    class="consent"
    role="dialog"
    aria-modal="false"
    aria-labelledby="consent-title"
  >
    <div class="consent__inner">
      <div class="consent__text">
        <p id="consent-title" class="consent__title">Analytics, if you are willing</p>
        <p class="consent__body">
          We would like to use Google Analytics to see which pages get used. Nothing loads and no
          cookies are set unless you say yes. Your documents are never part of it — see the
          <RouterLink to="/privacy">privacy notice</RouterLink>.
        </p>
      </div>

      <div class="consent__actions">
        <PwButton size="sm" @click="refuse">No thanks</PwButton>
        <PwButton size="sm" variant="solid" @click="accept">Allow analytics</PwButton>
      </div>
    </div>
  </div>
</template>

<style scoped>
.consent {
  position: fixed;
  inset: auto 0 0 0;
  z-index: 40;
  background: var(--bg-raised);
  border-top: 1px solid var(--border);
  box-shadow: var(--shadow-lg);
}

.consent__inner {
  max-width: var(--page-max);
  margin: 0 auto;
  padding: var(--s-4) var(--s-6);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--s-4) var(--s-6);
  flex-wrap: wrap;
}

.consent__text { max-width: 68ch; }

.consent__title {
  margin: 0 0 2px;
  font-size: var(--t-13);
  font-weight: var(--w-semi);
  color: var(--fg);
}

.consent__body {
  margin: 0;
  font-size: var(--t-12);
  line-height: var(--lh-snug);
  color: var(--fg-muted);
}

/* Both buttons the same size on purpose: the refusal has to be as easy to take as the accept. */
.consent__actions {
  display: flex;
  align-items: center;
  gap: var(--s-2);
  flex: none;
}

@media (max-width: 720px) {
  .consent__inner { padding: var(--s-3) var(--s-4); }
  .consent__actions { width: 100%; }
  .consent__actions > * { flex: 1 1 auto; }
}
</style>
