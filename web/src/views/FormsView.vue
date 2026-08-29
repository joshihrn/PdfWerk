<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import ResultPane from '../components/ResultPane.vue'
import {
  api,
  saveBlob,
  type Delivery,
  type DocumentResult,
  type FieldType,
  type PdfInfo,
} from '../api/client'

/**
 * A field as the designer holds it: PDF points, origin at the top-left of the page as rendered.
 * That is exactly the shape the API expects, so nothing is converted on submit — the only
 * transform anywhere is the display scale, which keeps the round trip honest.
 */
interface DraftField {
  id: number
  name: string
  type: FieldType
  page: number
  x: number
  y: number
  width: number
  height: number
  required: boolean
  readOnly: boolean
  multiline: boolean
  options: string
  toolTip: string
  /** Present on fields loaded from the document, absent on ones drawn here. */
  existing?: boolean
}

const files = ref<File[]>([])
const info = ref<PdfInfo | null>(null)
const loadError = ref<string | null>(null)
const loading = ref(false)

const fields = ref<DraftField[]>([])
const selectedId = ref<number | null>(null)
const currentPage = ref(1)
const scale = ref(1)
const nextId = ref(1)

const canvas = ref<HTMLCanvasElement>()
const surface = ref<HTMLDivElement>()

const result = ref<DocumentResult | null>(null)
const submitError = ref<unknown>(null)
const busy = ref(false)

const newFieldType = ref<FieldType>('Text')

const page = computed(() => info.value?.pages.find((p) => p.page === currentPage.value) ?? null)
const selected = computed(() => fields.value.find((f) => f.id === selectedId.value) ?? null)
const pageFields = computed(() => fields.value.filter((f) => f.page === currentPage.value))

// ---- loading -------------------------------------------------------------

watch(files, async (list) => {
  info.value = null
  fields.value = []
  result.value = null
  submitError.value = null
  loadError.value = null

  if (!list[0]) return

  loading.value = true
  try {
    const inspected = await api.inspect(list[0])
    info.value = inspected
    currentPage.value = 1

    // Existing fields load into the canvas so the designer edits a form rather than only
    // adding to one. Their coordinates come back in the same space they were sent in.
    fields.value = inspected.fields
      .filter((f) => f.rect !== null)
      .map((f) => ({
        id: nextId.value++,
        name: f.name,
        type: f.type,
        page: f.rect!.page,
        x: f.rect!.x,
        y: f.rect!.y,
        width: f.rect!.width,
        height: f.rect!.height,
        required: false,
        readOnly: f.readOnly,
        multiline: false,
        options: f.options.join('\n'),
        toolTip: '',
        existing: true,
      }))

    await nextTick()
    await renderPage()
  } catch (ex) {
    loadError.value = ex instanceof Error ? ex.message : String(ex)
  } finally {
    loading.value = false
  }
})

watch(currentPage, () => renderPage())

/**
 * Renders the page with pdf.js so fields are positioned against what the user actually sees.
 * Loaded on demand: it is by far the heaviest dependency here and only this view needs it.
 */
async function renderPage() {
  if (!files.value[0] || !canvas.value || !page.value) return

  const pdfjs = await import('pdfjs-dist')
  pdfjs.GlobalWorkerOptions.workerSrc = (await import('pdfjs-dist/build/pdf.worker.mjs?url')).default

  const data = await files.value[0].arrayBuffer()
  const document = await pdfjs.getDocument({ data }).promise
  const rendered = await document.getPage(currentPage.value)

  // Fit the page to the available width, but never blow it up past its natural size.
  const available = (surface.value?.clientWidth ?? 780) - 2   // less the 1px border each side
  const viewport = rendered.getViewport({ scale: 1 })
  scale.value = Math.min(available / viewport.width, 1.6)

  const scaled = rendered.getViewport({ scale: scale.value * window.devicePixelRatio })
  const context = canvas.value.getContext('2d')
  if (!context) return

  canvas.value.width = scaled.width
  canvas.value.height = scaled.height
  canvas.value.style.width = `${scaled.width / window.devicePixelRatio}px`
  canvas.value.style.height = `${scaled.height / window.devicePixelRatio}px`

  await rendered.render({ canvasContext: context, viewport: scaled, canvas: canvas.value }).promise
}

// ---- creating and moving -------------------------------------------------

const defaultSize: Record<FieldType, { width: number; height: number }> = {
  Text: { width: 200, height: 22 },
  Checkbox: { width: 16, height: 16 },
  RadioGroup: { width: 140, height: 16 },
  Dropdown: { width: 160, height: 20 },
  ListBox: { width: 160, height: 60 },
  Signature: { width: 180, height: 50 },
}

function addFieldAt(pointX: number, pointY: number) {
  const size = defaultSize[newFieldType.value]
  const base = newFieldType.value.toLowerCase()

  // Names must be unique within the form, so a suffix is appended until one is free.
  let name = base
  let counter = 1
  while (fields.value.some((f) => f.name === name)) name = `${base}${++counter}`

  const field: DraftField = {
    id: nextId.value++,
    name,
    type: newFieldType.value,
    page: currentPage.value,
    x: Math.round(pointX),
    y: Math.round(pointY),
    width: size.width,
    height: size.height,
    required: false,
    readOnly: false,
    multiline: false,
    options: newFieldType.value === 'Dropdown' || newFieldType.value === 'ListBox' || newFieldType.value === 'RadioGroup'
      ? 'Option A\nOption B'
      : '',
    toolTip: '',
  }

  fields.value.push(field)
  selectedId.value = field.id
}

function onSurfaceClick(event: MouseEvent) {
  if (!page.value || !canvas.value) return

  // Clicks that land on an existing field are handled by that field, not here.
  if ((event.target as HTMLElement).closest('.field')) return

  const bounds = canvas.value.getBoundingClientRect()
  const x = (event.clientX - bounds.left) / scale.value
  const y = (event.clientY - bounds.top) / scale.value

  if (x < 0 || y < 0 || x > page.value.width || y > page.value.height) return

  addFieldAt(x, y)
}

type DragMode = 'move' | 'resize'
let drag: { id: number; mode: DragMode; startX: number; startY: number; originX: number; originY: number; originW: number; originH: number } | null = null

function beginDrag(event: PointerEvent, field: DraftField, mode: DragMode) {
  event.stopPropagation()
  event.preventDefault()

  selectedId.value = field.id
  drag = {
    id: field.id,
    mode,
    startX: event.clientX,
    startY: event.clientY,
    originX: field.x,
    originY: field.y,
    originW: field.width,
    originH: field.height,
  }

  // Pointer capture keeps the drag alive even if the cursor leaves the element mid-gesture.
  ;(event.target as HTMLElement).setPointerCapture(event.pointerId)
  window.addEventListener('pointermove', onDragMove)
  window.addEventListener('pointerup', endDrag)
}

function onDragMove(event: PointerEvent) {
  if (!drag || !page.value) return

  const field = fields.value.find((f) => f.id === drag!.id)
  if (!field) return

  const dx = (event.clientX - drag.startX) / scale.value
  const dy = (event.clientY - drag.startY) / scale.value

  if (drag.mode === 'move') {
    // Clamped to the page: a widget outside the media box is invisible and confusing.
    field.x = Math.round(Math.max(0, Math.min(drag.originX + dx, page.value.width - field.width)))
    field.y = Math.round(Math.max(0, Math.min(drag.originY + dy, page.value.height - field.height)))
  } else {
    field.width = Math.round(Math.max(8, Math.min(drag.originW + dx, page.value.width - field.x)))
    field.height = Math.round(Math.max(8, Math.min(drag.originH + dy, page.value.height - field.y)))
  }
}

function endDrag() {
  drag = null
  window.removeEventListener('pointermove', onDragMove)
  window.removeEventListener('pointerup', endDrag)
}

function removeField(id: number) {
  fields.value = fields.value.filter((f) => f.id !== id)
  if (selectedId.value === id) selectedId.value = null
}

// ---- submitting ----------------------------------------------------------

function optionList(field: DraftField): string[] {
  return field.options
    .split('\n')
    .map((o) => o.trim())
    .filter((o) => o.length > 0)
}

async function applyDesign(delivery: Delivery) {
  if (!files.value[0]) return

  busy.value = true
  submitError.value = null

  try {
    const drawn = fields.value.filter((f) => !f.existing)

    const produced = await api.designFields(
      files.value[0],
      {
        add: drawn.map((f) => ({
          name: f.name,
          type: f.type,
          rect: { page: f.page, x: f.x, y: f.y, width: f.width, height: f.height },
          required: f.required,
          readOnly: f.readOnly,
          multiline: f.multiline,
          toolTip: f.toolTip || null,
          options: optionList(f),
        })),
        // A field loaded from the document and then deleted here must be removed there too.
        remove: (info.value?.fields ?? [])
          .map((f) => f.name)
          .filter((name) => !fields.value.some((f) => f.name === name)),
        replace: true,
      },
      delivery,
    )

    result.value = produced
    if (delivery === 'download') saveBlob(produced.blob, produced.fileName)
  } catch (ex) {
    result.value = null
    submitError.value = ex
  } finally {
    busy.value = false
  }
}

// ---- filling -------------------------------------------------------------

const fillValues = ref<Record<string, string>>({})
const flatten = ref(false)

async function fill(delivery: Delivery) {
  if (!files.value[0]) return

  busy.value = true
  submitError.value = null

  try {
    const produced = await api.fillForm(
      files.value[0],
      { values: fillValues.value, flatten: flatten.value, strictFieldNames: false },
      delivery,
    )

    result.value = produced
    if (delivery === 'download') saveBlob(produced.blob, produced.fileName)
  } catch (ex) {
    result.value = null
    submitError.value = ex
  } finally {
    busy.value = false
  }
}

const mode = ref<'design' | 'fill'>('design')
</script>

<template>
  <div>
    <h1>Form fields</h1>
    <p class="muted">
      Click the page to drop a field, then drag to position and resize it. Coordinates are sent in
      PDF points exactly as you place them.
    </p>

    <div class="panel">
      <FileDrop v-model="files" />
      <div v-if="loading" class="note info" style="margin-top: 12px"><span class="spinner"></span> Reading document…</div>
      <div v-else-if="loadError" class="note err" style="margin-top: 12px">{{ loadError }}</div>
    </div>

    <template v-if="info">
      <div class="btns" style="margin-bottom: 14px">
        <button class="btn" :class="{ primary: mode === 'design' }" @click="mode = 'design'">Design fields</button>
        <button class="btn" :class="{ primary: mode === 'fill' }" @click="mode = 'fill'">Fill values</button>
      </div>

      <!-- ---- design ---- -->
      <div v-if="mode === 'design'" class="split">
        <div class="panel" style="margin: 0">
          <div class="row" style="margin-bottom: 12px; align-items: center">
            <div style="flex: 0 1 200px">
              <label for="type">Field to place</label>
              <select id="type" v-model="newFieldType">
                <option>Text</option><option>Checkbox</option><option>Dropdown</option>
                <option>ListBox</option><option>RadioGroup</option><option>Signature</option>
              </select>
            </div>

            <div v-if="info.pageCount > 1" style="flex: 0 1 170px">
              <label for="page">Page</label>
              <select id="page" v-model.number="currentPage">
                <option v-for="p in info.pages" :key="p.page" :value="p.page">
                  Page {{ p.page }} of {{ info.pageCount }}
                </option>
              </select>
            </div>

            <div class="small muted" style="flex: 1 1 auto; text-align: right">
              Click an empty spot to add · drag to move · corner handle to resize
            </div>
          </div>

          <div ref="surface" class="designer-wrap">
          <div
            class="designer"
            @click="onSurfaceClick"
          >
            <canvas ref="canvas"></canvas>

            <div
              v-for="field in pageFields"
              :key="field.id"
              class="field"
              :class="{ selected: field.id === selectedId, existing: field.existing }"
              :style="{
                left: `${field.x * scale}px`,
                top: `${field.y * scale}px`,
                width: `${field.width * scale}px`,
                height: `${field.height * scale}px`,
              }"
              @pointerdown="beginDrag($event, field, 'move')"
              @click.stop="selectedId = field.id"
            >
              <span class="field-label">{{ field.name }}</span>
              <span class="handle" @pointerdown="beginDrag($event, field, 'resize')"></span>
            </div>
          </div>
          </div>

          <div class="btns">
            <button class="btn primary" :disabled="busy || !fields.length" @click="applyDesign('stream')">
              Apply &amp; preview
            </button>
            <button class="btn" :disabled="busy || !fields.length" @click="applyDesign('download')">Download</button>
          </div>
        </div>

        <div class="stack">
          <div class="panel" style="margin: 0">
            <h3>{{ selected ? 'Selected field' : 'Fields' }}</h3>

            <template v-if="selected">
              <label for="fname">Name</label>
              <input id="fname" v-model="selected.name" type="text" />

              <label for="ftip" style="margin-top: 10px">Tooltip</label>
              <input id="ftip" v-model="selected.toolTip" type="text" placeholder="Shown on hover" />

              <div class="row" style="margin-top: 10px">
                <div><label>X</label><input v-model.number="selected.x" type="number" /></div>
                <div><label>Y</label><input v-model.number="selected.y" type="number" /></div>
              </div>
              <div class="row" style="margin-top: 8px">
                <div><label>Width</label><input v-model.number="selected.width" type="number" /></div>
                <div><label>Height</label><input v-model.number="selected.height" type="number" /></div>
              </div>

              <template v-if="['Dropdown', 'ListBox', 'RadioGroup'].includes(selected.type)">
                <label for="fopts" style="margin-top: 10px">Options (one per line)</label>
                <textarea id="fopts" v-model="selected.options" style="min-height: 90px"></textarea>
              </template>

              <div class="stack" style="margin-top: 12px">
                <label style="display: flex; align-items: center; gap: 8px; cursor: pointer">
                  <input v-model="selected.required" type="checkbox" style="width: auto" /> <span>Required</span>
                </label>
                <label style="display: flex; align-items: center; gap: 8px; cursor: pointer">
                  <input v-model="selected.readOnly" type="checkbox" style="width: auto" /> <span>Read only</span>
                </label>
                <label v-if="selected.type === 'Text'" style="display: flex; align-items: center; gap: 8px; cursor: pointer">
                  <input v-model="selected.multiline" type="checkbox" style="width: auto" /> <span>Multiline</span>
                </label>
              </div>

              <div class="btns">
                <button class="btn small danger" @click="removeField(selected.id)">Delete field</button>
                <button class="btn small" @click="selectedId = null">Done</button>
              </div>
            </template>

            <template v-else>
              <p v-if="!fields.length" class="muted small" style="margin-bottom: 0">
                No fields yet. Click anywhere on the page to place one.
              </p>
              <table v-else>
                <tbody>
                  <tr v-for="field in fields" :key="field.id" style="cursor: pointer" @click="selectedId = field.id; currentPage = field.page">
                    <td><code>{{ field.name }}</code></td>
                    <td class="muted small">{{ field.type }}</td>
                    <td class="muted small">p{{ field.page }}</td>
                    <td v-if="field.existing"><span class="tag grey">existing</span></td>
                    <td v-else></td>
                  </tr>
                </tbody>
              </table>
            </template>
          </div>

          <ResultPane :result="result" :error="submitError" :busy="busy" />
        </div>
      </div>

      <!-- ---- fill ---- -->
      <div v-else class="split">
        <div class="panel" style="margin: 0">
          <h3>Values</h3>

          <p v-if="!info.fields.length" class="muted small">
            This document has no form fields yet. Design some first, download the result, then
            upload that to fill it.
          </p>

          <div v-else class="stack">
            <div v-for="field in info.fields" :key="field.name">
              <label :for="`v-${field.name}`">
                {{ field.name }}
                <span class="muted">· {{ field.type }}</span>
              </label>

              <select
                v-if="field.options.length"
                :id="`v-${field.name}`"
                v-model="fillValues[field.name]"
              >
                <option value="">—</option>
                <option v-for="option in field.options" :key="option" :value="option">{{ option }}</option>
              </select>

              <select v-else-if="field.type === 'Checkbox'" :id="`v-${field.name}`" v-model="fillValues[field.name]">
                <option value="">—</option>
                <option value="true">Checked</option>
                <option value="false">Unchecked</option>
              </select>

              <input v-else :id="`v-${field.name}`" v-model="fillValues[field.name]" type="text" :placeholder="field.value ?? ''" />
            </div>
          </div>

          <label style="margin-top: 14px; display: flex; align-items: center; gap: 8px; cursor: pointer">
            <input v-model="flatten" type="checkbox" style="width: auto" />
            <span>Flatten — bake values in and remove the form</span>
          </label>

          <div class="btns">
            <button class="btn primary" :disabled="busy || !info.fields.length" @click="fill('stream')">
              Fill &amp; preview
            </button>
            <button class="btn" :disabled="busy || !info.fields.length" @click="fill('download')">Download</button>
          </div>
        </div>

        <ResultPane :result="result" :error="submitError" :busy="busy" idle-hint="The filled document appears here." />
      </div>
    </template>
  </div>
</template>

<style scoped>
.designer-wrap { width: 100%; }

.designer {
  position: relative;
  display: inline-block;
  line-height: 0;
  border: 1px solid var(--line);
  border-radius: 8px;
  overflow: hidden;
  background: #fff;
  max-width: 100%;
  cursor: crosshair;
}

.field {
  position: absolute;
  border: 1.5px solid var(--accent);
  background: rgba(110, 168, 254, 0.16);
  border-radius: 2px;
  cursor: move;
  touch-action: none;
}

.field.existing {
  border-color: var(--accent-2);
  background: rgba(56, 211, 159, 0.14);
}

.field.selected {
  border-color: #fff;
  box-shadow: 0 0 0 2px rgba(110, 168, 254, 0.6);
  z-index: 2;
}

.field-label {
  position: absolute;
  top: -17px;
  left: -1px;
  font: 600 10px/1.4 ui-sans-serif, system-ui, sans-serif;
  background: var(--accent);
  color: #06122b;
  padding: 1px 5px;
  border-radius: 3px;
  white-space: nowrap;
  pointer-events: none;
}

.field.existing .field-label { background: var(--accent-2); }

.handle {
  position: absolute;
  right: -5px;
  bottom: -5px;
  width: 11px;
  height: 11px;
  background: #fff;
  border: 1.5px solid var(--accent);
  border-radius: 2px;
  cursor: nwse-resize;
  touch-action: none;
}
</style>
