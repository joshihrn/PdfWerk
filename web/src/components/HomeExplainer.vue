<script setup lang="ts">
/**
 * A silent, three-scene explainer: what the name means, how a request reaches a PDF, and what
 * the fourteen operations are. It exists because the hero above it states the product in one
 * sentence and the sections below it are reference material — nothing on the page currently
 * *narrates* the thing, in order, for someone who has just arrived.
 *
 * Deliberately not a video file. Every word is real DOM text, so it reads and indexes like the
 * rest of the page; a video would hide all of this behind a codec. It plays once, the moment it
 * scrolls into view, and settles — an explainer that loops forever reads as an advertisement,
 * which is the one thing this product's own copy goes out of its way not to sound like ("flat,
 * factual, no gradient").
 *
 * The three facts told here are the same three the rest of the homepage already states — the
 * bracket-mark rationale from the README, the "one POST, three ways in" line from the hero, and
 * the live action list also rendered as cards further down. This is that content in order and in
 * motion, not new claims invented for the animation.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { ActionDescriptor } from '../api/client'

const props = defineProps<{
  /** Reuses HomeView's already-fetched list rather than a second network round trip. */
  actions: ActionDescriptor[]
}>()

const root = ref<HTMLElement>()

/** 0 = not started, 1..3 = scene reached, 4 = finished and settled. */
const phase = ref(0)

const reducedMotion =
  typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches

let observer: IntersectionObserver | null = null
let timers: ReturnType<typeof setTimeout>[] = []
let started = false

function wait(ms: number) {
  return new Promise<void>((resolve) => {
    timers.push(setTimeout(resolve, ms))
  })
}

async function play() {
  if (started) return
  started = true

  if (reducedMotion) {
    // Nothing to watch, so nothing to wait for — go straight to the settled state.
    phase.value = 4
    return
  }

  phase.value = 1
  await wait(2600)
  phase.value = 2
  await wait(3400)
  phase.value = 3
  await wait(3800)
  phase.value = 4
}

function replay() {
  started = false
  phase.value = 0
  // A tick so the reveal transitions retrigger from a genuinely blank state rather than
  // jumping backwards through them.
  requestAnimationFrame(() => void play())
}

onMounted(() => {
  const el = root.value
  if (!el) return

  observer = new IntersectionObserver(
    (entries) => {
      if (entries.some((e) => e.isIntersecting)) void play()
    },
    { threshold: 0.35 },
  )
  observer.observe(el)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  timers.forEach(clearTimeout)
})

const integrations = [
  { glyph: 'POST', label: 'One request', detail: 'curl, or any HTTP client. Nothing to install.' },
  { glyph: '&lt;/&gt;', label: 'One script tag', detail: 'A shadow-root widget, dropped into your own page.' },
  { glyph: '⌘', label: 'This page', detail: 'The same fourteen operations, without writing a request.' },
]

// Every real operation, not a curated highlight reel — picking a "best six" would misstate what
// the product does for the sake of a tidier grid.
const features = computed(() => props.actions)
</script>

<template>
  <section ref="root" class="explainer" aria-label="How PdfWerk works" :data-phase="phase">
    <!-- ---- scene 1: the name ---- -->
    <div class="scene scene--mark" :class="{ 'is-in': phase >= 1 }">
      <svg class="mark" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <g fill="none" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
          <path class="mark__bracket" d="M6.4 3.4H3.6v17.2h2.8M17.6 3.4h2.8v17.2h-2.8" />
          <path class="mark__page" stroke="currentColor" d="M8.6 6.4h4.6L15.8 9v9H8.6z" />
          <path class="mark__fold" stroke="currentColor" d="M13.2 6.4V9h2.6" />
        </g>
      </svg>

      <p class="scene__word">
        Pdf<span class="scene__accent">Werk</span>
      </p>

      <p class="scene__line scene__line--1">Werk — German for a work, or a workshop.</p>
      <p class="scene__line scene__line--2">
        The mark is a page held inside code brackets: an API first, with a document on the end of
        it.
      </p>
    </div>

    <!-- ---- scene 2: how a request becomes a PDF ---- -->
    <div class="scene scene--how" :class="{ 'is-in': phase >= 2 }">
      <h3 class="scene__title">One POST. Three ways in.</h3>

      <ul class="paths">
        <li v-for="(path, i) in integrations" :key="path.label" class="path" :style="{ '--i': i }">
          <span class="path__glyph" aria-hidden="true" v-html="path.glyph" />
          <span class="path__text">
            <strong>{{ path.label }}</strong>
            <span>{{ path.detail }}</span>
          </span>
        </li>
      </ul>
    </div>

    <!-- ---- scene 3: what it does ---- -->
    <div class="scene scene--features" :class="{ 'is-in': phase >= 3 }">
      <h3 class="scene__title">{{ features.length || 'Fourteen' }} operations, one endpoint shape.</h3>

      <ul class="chips">
        <li
          v-for="(action, i) in features"
          :key="action.action"
          class="chip"
          :style="{ '--i': i }"
        >
          {{ action.title }}
        </li>
      </ul>
    </div>

    <button
      v-if="phase >= 4"
      type="button"
      class="replay"
      aria-label="Play the explainer again"
      @click="replay"
    >
      Replay
    </button>
  </section>
</template>

<style scoped>
.explainer {
  display: flex;
  flex-direction: column;
  gap: var(--s-8);
  padding: var(--s-8) 0;
  border-top: 1px solid var(--border);
  border-bottom: 1px solid var(--border);
}

.scene {
  opacity: 0;
  transform: translateY(6px);
  transition:
    opacity var(--base) var(--ease),
    transform var(--base) var(--ease);
}

.scene.is-in {
  opacity: 1;
  transform: none;
}

/* ---- scene 1: mark + name ------------------------------------------- */

.scene--mark {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--s-3);
}

.mark {
  width: 40px;
  height: 40px;
  color: var(--fg);
  flex: none;
}

.mark__bracket,
.mark__page,
.mark__fold {
  stroke: var(--link);
  stroke-dasharray: 40;
  stroke-dashoffset: 40;
  transition: stroke-dashoffset 900ms var(--ease);
}

.mark__page,
.mark__fold {
  stroke: currentColor;
}

.scene--mark.is-in .mark__bracket { stroke-dashoffset: 0; }
.scene--mark.is-in .mark__page { stroke-dashoffset: 0; transition-delay: 220ms; }
.scene--mark.is-in .mark__fold { stroke-dashoffset: 0; transition-delay: 420ms; }

.scene__word {
  margin: 0;
  font-size: var(--t-32);
  font-weight: var(--w-semi);
  letter-spacing: var(--track-tight);
  opacity: 0;
  transform: translateY(4px);
  transition: opacity var(--base) var(--ease), transform var(--base) var(--ease);
  transition-delay: 500ms;
}

.scene--mark.is-in .scene__word { opacity: 1; transform: none; }

.scene__accent { color: var(--link); }

.scene__line {
  margin: 0;
  font-size: var(--t-14);
  color: var(--fg-muted);
  max-width: 52ch;
  opacity: 0;
  transform: translateY(4px);
  transition: opacity var(--base) var(--ease), transform var(--base) var(--ease);
}

.scene--mark.is-in .scene__line--1 { opacity: 1; transform: none; transition-delay: 900ms; }
.scene--mark.is-in .scene__line--2 { opacity: 1; transform: none; transition-delay: 1300ms; }

/* ---- scene 2: integration paths -------------------------------------- */

.scene__title {
  margin: 0 0 var(--s-4);
  font-size: var(--t-20);
  font-weight: var(--w-semi);
  letter-spacing: var(--track-tight);
}

.paths {
  display: flex;
  flex-direction: column;
  gap: var(--s-3);
  margin: 0;
  padding: 0;
  list-style: none;
}

.path {
  display: flex;
  align-items: center;
  gap: var(--s-4);
  opacity: 0;
  transform: translateY(4px);
  transition: opacity var(--base) var(--ease), transform var(--base) var(--ease);
  transition-delay: calc(var(--i) * 160ms);
}

.scene--how.is-in .path { opacity: 1; transform: none; }

.path__glyph {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  flex: none;
  font-family: var(--mono);
  font-size: var(--t-12);
  font-weight: var(--w-medium);
  color: var(--link);
  background: var(--bg-sunken);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
}

.path__text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  font-size: var(--t-13);
}

.path__text strong { font-weight: var(--w-medium); }
.path__text span { color: var(--fg-subtle); font-size: var(--t-12); }

/* ---- scene 3: the feature list ---------------------------------------- */

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.chip {
  padding: var(--s-2) var(--s-3);
  font-size: var(--t-13);
  color: var(--fg);
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: var(--r-full);
  opacity: 0;
  transform: translateY(4px);
  transition: opacity var(--base) var(--ease), transform var(--base) var(--ease);
  transition-delay: calc(var(--i) * 55ms);
}

.scene--features.is-in .chip { opacity: 1; transform: none; }

.replay {
  align-self: flex-start;
  padding: var(--s-1) var(--s-2);
  font-size: var(--t-12);
  color: var(--fg-subtle);
  background: none;
  border: 1px solid transparent;
  border-radius: var(--r-sm);
  cursor: pointer;
  transition: color var(--fast) var(--ease), border-color var(--fast) var(--ease);
}

.replay:hover,
.replay:focus-visible {
  color: var(--fg);
  border-color: var(--border);
}
</style>
