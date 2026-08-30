<script setup lang="ts">
/**
 * Renders a page and lets a word be clicked and rewritten in place.
 *
 * What this is, precisely: the text runs pdf.js reports are laid over the rendered page, and
 * clicking one opens an input positioned exactly on top of it. Editing it emits the original and
 * the new string, which the caller turns into a replacement scoped to that page.
 *
 * What it is not: a layout engine. The replacement still goes through the same find-and-replace
 * the API has always done, so a longer word does not reflow the paragraph around it. That
 * limitation is real and is stated in the UI rather than left to be discovered — but it removes
 * the part people actually got wrong, which was having to retype a string exactly, invisibly
 * including the ligatures and odd spacing a PDF often holds.
 */
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps<{
  file: File | null
  /** Text already spoken for by a replacement, so those runs can be marked as edited. */
  editedRuns?: Record<string, string>
  /**
   * Clicking empty space adds text there instead of doing nothing.
   *
   * Off by default: on the editing screen a stray click should not silently start writing on
   * someone's document.
   */
  allowPlacement?: boolean
  /** Text already placed, drawn as ghosts so it can be seen before the document is rendered. */
  placed?: { page: number; x: number; y: number; text: string; fontSize: number }[]
}>()

const emit = defineEmits<{
  pick: [payload: { find: string; replace: string; page: number }]
  place: [payload: { page: number; x: number; y: number }]
  pages: [count: number]
}>()

interface TextRun {
  id: string
  text: string
  /** Screen pixels, relative to the canvas box. */
  left: number
  top: number
  width: number
  height: number
}

const canvas = ref<HTMLCanvasElement>()
const wrap = ref<HTMLElement>()

const runs = ref<TextRun[]>([])
const pageCount = ref(0)
const currentPage = ref(1)
const rendering = ref(false)

/** Screen pixels per PDF point, so a click can be reported in the document's own units. */
const pageScale = ref(1)
const failure = ref<string | null>(null)

const editingId = ref<string | null>(null)
const draft = ref('')

const editing = computed(() => runs.value.find((r) => r.id === editingId.value) ?? null)

/**
 * A run is worth offering only if replacing it could work.
 *
 * Whitespace-only runs and single punctuation marks would match everywhere in the document, so
 * clicking one would rewrite far more than the thing that was clicked.
 */
function isEditable(text: string) {
  return text.trim().length > 1 && /[\p{L}\p{N}]/u.test(text)
}

function measureAvailableWidth() {
  const host = wrap.value
  if (!host) return 720

  // clientWidth includes padding, so subtract it: asking for a canvas wider than the content
  // box gets it clamped by max-width, which silently shifts every overlay position.
  const style = getComputedStyle(host)
  const padding = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight)
  return Math.max(host.clientWidth - padding, 240)
}

async function render() {
  if (!props.file || !canvas.value) return

  rendering.value = true
  failure.value = null
  editingId.value = null

  try {
    const pdfjs = await import('pdfjs-dist')
    pdfjs.GlobalWorkerOptions.workerSrc = (await import('pdfjs-dist/build/pdf.worker.mjs?url')).default

    const data = await props.file.arrayBuffer()
    const document = await pdfjs.getDocument({ data }).promise

    pageCount.value = document.numPages
    emit('pages', document.numPages)

    if (currentPage.value > document.numPages) currentPage.value = 1

    const page = await document.getPage(currentPage.value)

    // Re-read after the awaits: the component can be unmounted during any of them.
    const target = canvas.value
    if (!target) return

    const base = page.getViewport({ scale: 1 })
    const scale = Math.min(measureAvailableWidth() / base.width, 1.5)
    pageScale.value = scale
    const viewport = page.getViewport({ scale: scale * window.devicePixelRatio })

    const context = target.getContext('2d')
    if (!context) return

    target.width = viewport.width
    target.height = viewport.height
    target.style.width = `${viewport.width / window.devicePixelRatio}px`
    target.style.height = `${viewport.height / window.devicePixelRatio}px`

    await page.render({ canvasContext: context, viewport, canvas: target }).promise
    if (canvas.value !== target) return

    // Positions come from the same viewport the page was drawn with, at CSS scale rather than
    // device scale, so the overlay lines up whatever the display's pixel ratio.
    const cssViewport = page.getViewport({ scale })
    const content = await page.getTextContent()
    if (canvas.value !== target) return

    runs.value = content.items.flatMap((item, index) => {
      if (!('str' in item) || !isEditable(item.str)) return []

      // transform is [a, b, c, d, e, f]; e and f are the origin, and d approximates the height.
      const [, , , d, e, f] = item.transform as number[]
      const height = Math.abs(d) * scale
      const [x, y] = cssViewport.convertToViewportPoint(e, f)

      return [{
        id: `${currentPage.value}:${index}`,
        text: item.str,
        left: x,
        // convertToViewportPoint gives the baseline; the box starts a line-height above it.
        top: y - height,
        width: ('width' in item ? (item.width as number) : 0) * scale,
        height,
      }]
    })
  } catch (ex) {
    failure.value = ex instanceof Error ? ex.message : String(ex)
    runs.value = []
  } finally {
    rendering.value = false
  }
}

function beginEdit(run: TextRun) {
  editingId.value = run.id
  draft.value = props.editedRuns?.[run.text] ?? run.text
  nextTick(() => document.getElementById('pw-inline-edit')?.focus())
}

function commit() {
  const run = editing.value
  if (!run) return

  const replacement = draft.value

  // An unchanged value is not an edit. Emitting one would add a replacement that rewrites a
  // string to itself, spending a match and reporting a change that did not happen.
  if (replacement !== run.text) {
    emit('pick', { find: run.text, replace: replacement, page: currentPage.value })
  }

  editingId.value = null
}

function cancel() {
  editingId.value = null
}

/**
 * A click on bare page, reported in PDF points from the top-left.
 *
 * Runs are buttons that stop the event, so anything reaching here is genuinely empty space.
 */
function placeAt(event: MouseEvent) {
  if (!props.allowPlacement) return

  const box = (event.currentTarget as HTMLElement).getBoundingClientRect()

  emit('place', {
    page: currentPage.value,
    x: Math.round((event.clientX - box.left) / pageScale.value),
    y: Math.round((event.clientY - box.top) / pageScale.value),
  })
}

/** Placed text belonging to the page on screen, positioned in screen pixels. */
const ghosts = computed(() =>
  (props.placed ?? [])
    .filter((p) => p.page === currentPage.value)
    .map((p, index) => ({
      key: `${p.page}:${index}`,
      text: p.text,
      left: p.x * pageScale.value,
      top: p.y * pageScale.value,
      size: p.fontSize * pageScale.value,
    })),
)

watch(() => props.file, () => {
  currentPage.value = 1
  void render()
})

watch(currentPage, () => void render())

const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => void render())

watch(wrap, (host) => {
  observer?.disconnect()
  if (host) observer?.observe(host)
})

onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div class="picker">
    <div v-if="pageCount > 1" class="picker__bar">
      <button type="button" :disabled="currentPage <= 1" @click="currentPage--">Previous</button>
      <span aria-live="polite">Page {{ currentPage }} of {{ pageCount }}</span>
      <button type="button" :disabled="currentPage >= pageCount" @click="currentPage++">Next</button>
    </div>

    <p v-if="failure" role="alert" class="picker__error">{{ failure }}</p>

    <div
      ref="wrap"
      class="picker__page"
      :class="{ 'picker__page--placing': allowPlacement }"
      :data-ready="!rendering"
      @click="placeAt"
    >
      <canvas ref="canvas" />

      <span
        v-for="ghost in ghosts"
        :key="ghost.key"
        class="picker__ghost"
        :style="{ left: `${ghost.left}px`, top: `${ghost.top}px`, fontSize: `${ghost.size}px` }"
        aria-hidden="true"
      >{{ ghost.text }}</span>

      <button
        v-for="run in runs"
        :key="run.id"
        type="button"
        class="picker__run"
        :class="{ 'picker__run--edited': editedRuns?.[run.text] !== undefined }"
        :style="{
          left: `${run.left}px`,
          top: `${run.top}px`,
          width: `${run.width}px`,
          height: `${run.height}px`,
        }"
        :aria-label="`Edit “${run.text}”`"
        @click.stop="beginEdit(run)"
      />

      <input
        v-if="editing"
        id="pw-inline-edit"
        v-model="draft"
        class="picker__input"
        :style="{
          left: `${editing.left}px`,
          top: `${editing.top}px`,
          minWidth: `${Math.max(editing.width, 80)}px`,
          height: `${editing.height}px`,
          fontSize: `${Math.max(editing.height * 0.8, 9)}px`,
        }"
        :aria-label="`Replacement for “${editing.text}”`"
        @keydown.enter.prevent="commit"
        @keydown.esc.prevent="cancel"
        @blur="commit"
      />
    </div>
  </div>
</template>

<style scoped>
.picker__bar {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  margin-bottom: var(--s-3);
  font-size: var(--t-13);
  color: var(--fg-muted);
}

.picker__bar button {
  padding: var(--s-1) var(--s-3);
  font: inherit;
  color: inherit;
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: var(--r-sm);
  cursor: pointer;
}

.picker__bar button:disabled { opacity: 0.5; cursor: default; }

.picker__error {
  margin: 0 0 var(--s-3);
  font-size: var(--t-13);
  color: var(--bad-fg);
}

.picker__page {
  position: relative;
  display: inline-block;
  max-width: 100%;
  line-height: 0;
}

.picker__page canvas {
  max-width: 100%;
  border: 1px solid var(--border);
  border-radius: var(--r-sm);
}

/*
 * Transparent until hovered. The point is to read the page normally and reach for a word when
 * one needs changing; boxes drawn around every run would make the page unreadable.
 */
.picker__run {
  position: absolute;
  padding: 0;
  background: transparent;
  border: 0;
  border-radius: 2px;
  cursor: text;
}

.picker__run:hover,
.picker__run:focus-visible {
  background: color-mix(in srgb, var(--link) 22%, transparent);
  outline: 1px solid var(--link);
}

/* Already spoken for by a replacement, so it reads as changed without being opened. */
.picker__run--edited {
  background: color-mix(in srgb, var(--ok-fg) 20%, transparent);
}

/* A text cursor over the page, so it reads as somewhere you can write. */
.picker__page--placing { cursor: text; }

/*
 * Placed text is shown before the document is rendered, so its position can be judged without a
 * round trip. It is an approximation of the final drawing, not the drawing itself.
 */
.picker__ghost {
  position: absolute;
  line-height: 1.2;
  color: var(--fg);
  white-space: pre;
  pointer-events: none;
  background: color-mix(in srgb, var(--link) 12%, transparent);
  outline: 1px dashed color-mix(in srgb, var(--link) 55%, transparent);
}

.picker__input {
  position: absolute;
  padding: 0 2px;
  font-family: inherit;
  line-height: 1;
  color: var(--fg);
  background: var(--bg);
  border: 1px solid var(--link);
  border-radius: 2px;
}

@media (prefers-reduced-motion: no-preference) {
  .picker__run { transition: background-color 90ms ease; }
}
</style>
