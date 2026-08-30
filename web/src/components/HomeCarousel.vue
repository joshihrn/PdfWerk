<script setup lang="ts">
/**
 * A six-step, step-by-step walkthrough: what the product is, why it's named that, what it does,
 * a few things worth knowing, how the page above calls the same API you can, and how to embed
 * it elsewhere. Replaces an earlier attempt (HomeExplainer) that played once on scroll and was
 * not what was asked for — this one is a proper carousel: numbered, navigable, auto-advancing.
 *
 * Every fact on every slide is sourced from elsewhere in this codebase, not invented for the
 * carousel: the brand line is quoted from the README, the operation list and count come from the
 * same live api.actions() call the Operations grid below uses, and the code samples are the
 * exact snippets already shown in "Two ways to integrate" — copied, not paraphrased, so the two
 * never quietly disagree with each other.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ActionDescriptor } from '../api/client'

const props = defineProps<{
  actions: ActionDescriptor[]
}>()

const SLIDE_MS = 20_000 // six slides at 20s is the two minutes this was asked to run.

const slides = [
  { eyebrow: '01 · What it is', label: 'What is PdfWerk' },
  { eyebrow: '02 · The name', label: 'Why "PdfWerk"' },
  { eyebrow: '03 · Features', label: 'What it does' },
  { eyebrow: '04 · Worth knowing', label: 'A few things worth knowing' },
  { eyebrow: '05 · Under the hood', label: 'One API, same endpoints' },
  { eyebrow: '06 · Embed it', label: 'Drop it into any page' },
]

const active = ref(0)
const userControlled = ref(false)
const paused = ref(false)
const progressKey = ref(0)

const reducedMotion =
  typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches

const autoplaying = computed(() => !reducedMotion && !userControlled.value && !paused.value)

let timer: ReturnType<typeof setInterval> | null = null

function armTimer() {
  if (timer) clearInterval(timer)
  timer = autoplaying.value ? setInterval(() => goTo(active.value + 1, false), SLIDE_MS) : null
}

function goTo(index: number, manual = true) {
  const next = ((index % slides.length) + slides.length) % slides.length
  active.value = next
  progressKey.value++
  if (manual) userControlled.value = true
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'ArrowRight') goTo(active.value + 1)
  else if (event.key === 'ArrowLeft') goTo(active.value - 1)
  else return
  event.preventDefault()
}

watch(autoplaying, armTimer)
onMounted(armTimer)
onBeforeUnmount(() => timer && clearInterval(timer))

// ---- slide 3: the same fourteen operations, grouped for one screen rather than a wall of chips.
const GROUPS: { label: string; match: (action: string) => boolean }[] = [
  { label: 'Create', match: (a) => a.startsWith('CreateFrom') },
  { label: 'Edit', match: (a) => a === 'EditText' || a === 'Annotate' },
  { label: 'Forms', match: (a) => a === 'EditFormFields' || a === 'FillForm' },
  { label: 'Combine', match: (a) => a === 'Merge' },
  { label: 'Organise', match: (a) => ['Split', 'Rotate', 'Watermark', 'Protect'].includes(a) },
  { label: 'Understand', match: (a) => a === 'Inspect' },
  { label: 'AI', match: (a) => a === 'Summarize' || a === 'DraftDocument' },
]

const grouped = computed(() =>
  GROUPS.map((g) => ({ label: g.label, items: props.actions.filter((a) => g.match(a.action)) }))
    .filter((g) => g.items.length > 0),
)

// ---- slide 4: real, sourced facts — the same ones stated in the hero and README, not new claims.
const facts = [
  { stat: '4.7 KB', detail: 'gzipped', note: 'The whole embeddable widget. Smaller than most of the icons on this page.' },
  { stat: 'Optional', detail: 'infrastructure', note: 'No Redis or Postgres configured? It falls back to SQLite and in-process rate limits on its own.' },
  { stat: 'MIT / Apache', detail: 'every dependency', note: 'No copyleft anywhere in the tree, and nothing that changes terms above a revenue threshold.' },
  { stat: 'Offline-capable', detail: 'summarisation', note: 'Point it at a local Ollama model and no document ever leaves your machine.' },
]
</script>

<template>
  <section
    class="carousel"
    role="region"
    aria-roledescription="carousel"
    aria-label="How PdfWerk works, in six steps"
    tabindex="0"
    @keydown="onKeydown"
    @mouseenter="paused = true"
    @mouseleave="paused = false"
    @focusin="paused = true"
    @focusout="paused = false"
  >
    <p class="sr-only" aria-live="polite">Slide {{ active + 1 }} of {{ slides.length }}: {{ slides[active].label }}</p>

    <h2 class="section-title">How it works</h2>

    <div class="carousel__body">
      <!-- ---- rail: numbered index, doubles as slide navigation ---- -->
      <ol class="rail" aria-label="Slides">
        <li v-for="(slide, i) in slides" :key="slide.label">
          <button
            type="button"
            class="rail__item"
            :class="{ 'is-active': i === active }"
            :aria-current="i === active ? 'true' : undefined"
            @click="goTo(i)"
          >
            <span class="rail__index">{{ String(i + 1).padStart(2, '0') }}</span>
            <span class="rail__label">{{ slide.label }}</span>
          </button>
        </li>
      </ol>

      <!-- ---- the slide viewport ---- -->
      <div class="stage">
        <div class="stage__track" :style="{ transform: `translateX(-${active * 100}%)` }">
          <!-- ============ 1. what it is ============ -->
          <article class="slide" role="group" :aria-hidden="active !== 0" aria-roledescription="slide" aria-label="Slide 1 of 6: What is PdfWerk">
            <p class="slide__eyebrow">{{ slides[0].eyebrow }}</p>
            <h3 class="slide__title">PDF operations as an HTTP API</h3>
            <p class="slide__body">
              Create, edit, split, merge, fill and summarise PDFs. Every operation is one POST —
              through this page, a REST endpoint, or a widget dropped into your own app.
            </p>

            <div class="pipeline" aria-hidden="true">
              <span class="pipeline__node">Text · Word · PDF</span>
              <span class="pipeline__arrow">
                <code class="pipeline__label">POST</code>
                <svg viewBox="0 0 40 8" preserveAspectRatio="none"><path d="M0 4h36m0 0-6-3.5M36 4l-6 3.5" fill="none" stroke="currentColor" stroke-width="1.4" /></svg>
              </span>
              <span class="pipeline__node pipeline__node--accent">PdfWerk</span>
              <span class="pipeline__arrow">
                <svg viewBox="0 0 40 8" preserveAspectRatio="none"><path d="M0 4h36m0 0-6-3.5M36 4l-6 3.5" fill="none" stroke="currentColor" stroke-width="1.4" /></svg>
              </span>
              <span class="pipeline__node">PDF</span>
            </div>
          </article>

          <!-- ============ 2. the name ============ -->
          <article class="slide" role="group" :aria-hidden="active !== 1" aria-roledescription="slide" aria-label="Slide 2 of 6: Why PdfWerk">
            <p class="slide__eyebrow">{{ slides[1].eyebrow }}</p>

            <div class="name-block">
              <svg class="mark" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                <g fill="none" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
                  <path class="mark__bracket" d="M6.4 3.4H3.6v17.2h2.8M17.6 3.4h2.8v17.2h-2.8" />
                  <path stroke="currentColor" d="M8.6 6.4h4.6L15.8 9v9H8.6z" />
                  <path stroke="currentColor" d="M13.2 6.4V9h2.6" />
                </g>
              </svg>
              <h3 class="slide__title slide__title--word">Pdf<span class="accent">Werk</span></h3>
            </div>

            <p class="slide__body">Werk — German for a work, or a workshop.</p>
            <p class="slide__body slide__body--muted">
              The mark is a page held inside code brackets: an API first, with a document on the
              end of it.
            </p>
          </article>

          <!-- ============ 3. features ============ -->
          <article class="slide" role="group" :aria-hidden="active !== 2" aria-roledescription="slide" aria-label="Slide 3 of 6: What it does">
            <p class="slide__eyebrow">{{ slides[2].eyebrow }}</p>
            <h3 class="slide__title">{{ actions.length || 14 }} operations, one endpoint shape</h3>

            <div class="feature-groups">
              <div v-for="group in grouped" :key="group.label" class="feature-group">
                <p class="feature-group__label">{{ group.label }}</p>
                <ul class="feature-group__items">
                  <li v-for="item in group.items" :key="item.action">{{ item.title }}</li>
                </ul>
              </div>
            </div>
          </article>

          <!-- ============ 4. cool stuff ============ -->
          <article class="slide" role="group" :aria-hidden="active !== 3" aria-roledescription="slide" aria-label="Slide 4 of 6: A few things worth knowing">
            <p class="slide__eyebrow">{{ slides[3].eyebrow }}</p>
            <h3 class="slide__title">A few things worth knowing</h3>

            <div class="facts-grid">
              <div v-for="fact in facts" :key="fact.stat" class="fact-card">
                <p class="fact-card__stat">{{ fact.stat }} <span>{{ fact.detail }}</span></p>
                <p class="fact-card__note">{{ fact.note }}</p>
              </div>
            </div>
          </article>

          <!-- ============ 5. api ============ -->
          <article class="slide" role="group" :aria-hidden="active !== 4" aria-roledescription="slide" aria-label="Slide 5 of 6: One API, same endpoints">
            <p class="slide__eyebrow">{{ slides[4].eyebrow }}</p>
            <h3 class="slide__title">This page calls the same API you can</h3>
            <p class="slide__body">
              Every tool above is a thin form around one HTTP request. Click
              <RouterLink to="/create">Create</RouterLink>, and this is exactly what runs:
            </p>

            <pre><code>curl -X POST https://pdfwerk.com/v1/create/text \
  -H 'X-Api-Key: pw_…' \
  -H 'Content-Type: application/json' \
  -d '{"content":"# Invoice","format":"Markdown"}' \
  -o invoice.pdf</code></pre>

            <p class="slide__body slide__body--muted">
              Every operation on the site works the same way —
              <a href="/docs" target="_blank" rel="noopener">the full reference is at /docs</a>.
            </p>
          </article>

          <!-- ============ 6. embed ============ -->
          <article class="slide" role="group" :aria-hidden="active !== 5" aria-roledescription="slide" aria-label="Slide 6 of 6: Drop it into any page">
            <p class="slide__eyebrow">{{ slides[5].eyebrow }}</p>
            <h3 class="slide__title">One script tag, anywhere</h3>

            <pre><code>&lt;div id="pdf"&gt;&lt;/div&gt;
&lt;script src="/pdfwerk-embed.js"&gt;&lt;/script&gt;
&lt;script&gt;
  PdfWerk.mount('#pdf', {
    tool: 'create',
    delivery: 'preview',
  })
&lt;/script&gt;</code></pre>

            <p class="slide__body slide__body--muted">
              Rendered in a shadow root, so your page's styles and the widget's cannot touch each
              other —
              <a href="/embed-demo.html" target="_blank" rel="noopener">every tool, running live</a>.
            </p>
          </article>
        </div>
      </div>
    </div>

    <!-- ---- controls ---- -->
    <div class="controls">
      <button type="button" class="controls__arrow" aria-label="Previous slide" @click="goTo(active - 1)">‹</button>

      <div class="progress" aria-hidden="true">
        <div :key="progressKey" class="progress__fill" :class="{ 'is-playing': autoplaying }" />
      </div>

      <button type="button" class="controls__arrow" aria-label="Next slide" @click="goTo(active + 1)">›</button>
    </div>
  </section>
</template>

<style scoped>
.carousel {
  padding: var(--s-8) 0;
  border-top: 1px solid var(--border);
  border-bottom: 1px solid var(--border);
}

.carousel:focus-visible {
  outline: 2px solid var(--focus);
  outline-offset: 4px;
  border-radius: var(--r-md);
}

.carousel__body {
  display: grid;
  grid-template-columns: 200px 1fr;
  gap: var(--s-8);
}

/* ---- rail: the numbered index and the primary navigation, doing both jobs at once rather
   than duplicating the step list as plain text somewhere and dots somewhere else. ---- */

.rail {
  display: flex;
  flex-direction: column;
  gap: var(--s-1);
  margin: 0;
  padding: 0;
  list-style: none;
}

.rail__item {
  display: flex;
  align-items: baseline;
  gap: var(--s-3);
  width: 100%;
  padding: var(--s-2) var(--s-3);
  text-align: left;
  color: var(--fg-subtle);
  background: none;
  border: 0;
  border-left: 2px solid transparent;
  border-radius: 0 var(--r-sm) var(--r-sm) 0;
  cursor: pointer;
  transition: color var(--fast) var(--ease), border-color var(--fast) var(--ease), background-color var(--fast) var(--ease);
}

.rail__item:hover { color: var(--fg); background: var(--bg-sunken); }

.rail__item.is-active {
  color: var(--fg);
  border-left-color: var(--link);
  background: var(--bg-sunken);
}

.rail__index {
  font-family: var(--mono);
  font-size: var(--t-11);
  color: var(--fg-subtle);
}

.rail__item.is-active .rail__index { color: var(--link); }

.rail__label {
  font-size: var(--t-13);
  font-weight: var(--w-medium);
  line-height: var(--lh-snug);
}

/* ---- stage: the sliding viewport ---- */

.stage {
  overflow: hidden;
  border: 1px solid var(--border);
  border-radius: var(--r-xl);
  background: var(--bg-raised);
  box-shadow: var(--shadow-xs);
}

.stage__track {
  display: flex;
  /* Longer than the token pair used for hover/focus micro-feedback elsewhere in the app: this
     is a full scene change, not a one-control state change, so it earns a slower, still-brisk
     move. Reduced motion already collapses this globally via base.css. */
  transition: transform 420ms var(--ease);
}

.slide {
  flex: 0 0 100%;
  min-width: 0;
  padding: var(--s-8);
  min-height: 360px;
  display: flex;
  flex-direction: column;
  gap: var(--s-3);
}

.slide__eyebrow {
  font-size: var(--t-11);
  font-weight: var(--w-semi);
  text-transform: uppercase;
  letter-spacing: var(--track-wide);
  color: var(--link);
  margin: 0;
}

.slide__title {
  font-size: var(--t-24);
  font-weight: var(--w-semi);
  letter-spacing: var(--track-tight);
  margin: 0;
  max-width: 32ch;
}

.slide__title--word { font-size: var(--t-32); }

.slide__body {
  font-size: var(--t-14);
  color: var(--fg-muted);
  line-height: var(--lh-snug);
  max-width: 58ch;
  margin: 0;
}

.slide__body--muted { color: var(--fg-subtle); font-size: var(--t-13); }

.slide pre { margin: var(--s-1) 0; max-width: 58ch; }

.accent { color: var(--link); }

/* ---- slide 1: pipeline ---- */

.pipeline {
  display: flex;
  align-items: center;
  gap: var(--s-2);
  margin-top: var(--s-4);
  flex-wrap: wrap;
}

.pipeline__node {
  padding: var(--s-2) var(--s-4);
  font-size: var(--t-13);
  font-weight: var(--w-medium);
  background: var(--bg-sunken);
  border: 1px solid var(--border);
  border-radius: var(--r-full);
  white-space: nowrap;
}

.pipeline__node--accent {
  color: var(--link);
  border-color: var(--link);
  background: color-mix(in srgb, var(--link) 10%, transparent);
}

.pipeline__arrow {
  position: relative;
  display: flex;
  align-items: center;
  width: 44px;
  height: 16px;
  color: var(--fg-subtle);
}

.pipeline__arrow svg { width: 100%; height: 100%; }

.pipeline__label {
  position: absolute;
  top: -18px;
  left: 50%;
  transform: translateX(-50%);
  font-size: var(--t-11);
  color: var(--fg-subtle);
  background: none;
  padding: 0;
}

/* ---- slide 2: name ---- */

.name-block {
  display: flex;
  align-items: center;
  gap: var(--s-4);
}

.mark { width: 56px; height: 56px; color: var(--fg); flex: none; }
.mark__bracket { stroke: var(--link); }

/* ---- slide 3: feature groups ---- */

.feature-groups {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: var(--s-5);
  margin-top: var(--s-2);
}

.feature-group__label {
  margin: 0 0 var(--s-2);
  font-size: var(--t-11);
  font-weight: var(--w-semi);
  text-transform: uppercase;
  letter-spacing: var(--track-wide);
  color: var(--fg-subtle);
}

.feature-group__items {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--s-1);
}

.feature-group__items li {
  font-size: var(--t-13);
  color: var(--fg);
  line-height: var(--lh-snug);
}

/* ---- slide 4: facts ---- */

.facts-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--s-3);
  margin-top: var(--s-2);
}

.fact-card {
  padding: var(--s-4);
  background: var(--bg-sunken);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
}

.fact-card__stat {
  margin: 0 0 var(--s-1);
  font-size: var(--t-16);
  font-weight: var(--w-semi);
  letter-spacing: var(--track-tight);
}

.fact-card__stat span {
  font-size: var(--t-12);
  font-weight: var(--w-regular);
  color: var(--fg-subtle);
}

.fact-card__note {
  margin: 0;
  font-size: var(--t-12);
  color: var(--fg-subtle);
  line-height: var(--lh-base);
}

/* ---- controls ---- */

.controls {
  display: flex;
  align-items: center;
  gap: var(--s-4);
  margin-top: var(--s-4);
  padding-left: calc(200px + var(--s-8));
}

.controls__arrow {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  flex: none;
  font-size: var(--t-20);
  line-height: 1;
  color: var(--fg-muted);
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: var(--r-full);
  cursor: pointer;
  transition: color var(--fast) var(--ease), border-color var(--fast) var(--ease), transform var(--fast) var(--ease);
}

.controls__arrow:hover { color: var(--fg); border-color: var(--border-strong); }
.controls__arrow:active { transform: scale(0.94); }

.progress {
  flex: 1 1 auto;
  height: 2px;
  background: var(--border);
  border-radius: var(--r-full);
  overflow: hidden;
}

.progress__fill {
  height: 100%;
  width: 0%;
  background: var(--link);
}

/* Only animates while genuinely autoplaying — paused or manually navigated leaves a bar that
   does not silently keep moving in the background. */
.progress__fill.is-playing {
  animation: progress-fill 20000ms linear forwards;
}

@keyframes progress-fill {
  from { width: 0%; }
  to { width: 100%; }
}

@media (max-width: 880px) {
  .carousel__body { grid-template-columns: 1fr; gap: var(--s-4); }

  .rail {
    flex-direction: row;
    flex-wrap: wrap;
    gap: var(--s-2);
  }

  .rail__item {
    flex-direction: column;
    align-items: flex-start;
    gap: var(--s-1);
    width: auto;
    border-left: 0;
    border-bottom: 2px solid transparent;
    border-radius: var(--r-sm) var(--r-sm) 0 0;
  }

  .rail__item.is-active { border-left-color: transparent; border-bottom-color: var(--link); }
  .rail__label { display: none; }

  .slide { padding: var(--s-5); min-height: 420px; }
  .controls { padding-left: 0; }
  .facts-grid { grid-template-columns: 1fr; }
}
</style>
