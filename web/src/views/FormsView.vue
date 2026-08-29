<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import ResultPane from '../components/ResultPane.vue'
import {
  PwBadge,
  PwButton,
  PwCallout,
  PwCard,
  PwCheckbox,
  PwField,
  PwInput,
  PwPageHeader,
  PwSegmented,
  PwSelect,
  PwSpinner,
  PwTextarea,
} from '../components/ui'
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

const mode = ref('design')
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

const fillValues = ref<Record<string, string>>({})
const flatten = ref(false)

const page = computed(() => info.value?.pages.find((p) => p.page === currentPage.value) ?? null)
const selected = computed(() => fields.value.find((f) => f.id === selectedId.value) ?? null)
const pageFields = computed(() => fields.value.filter((f) => f.page === currentPage.value))
const drawn = computed(() => fields.value.filter((f) => !f.existing))

const modes = [
  { value: 'design', label: 'Design fields' },
  { value: 'fill', label: 'Fill values' },
]

const fieldTypes: { value: FieldType; label: string }[] = [
  { value: 'Text', label: 'Text' },
  { value: 'Checkbox', label: 'Checkbox' },
  { value: 'Dropdown', label: 'Dropdown' },
  { value: 'ListBox', label: 'List box' },
  { value: 'RadioGroup', label: 'Radio group' },
  { value: 'Signature', label: 'Signature' },
]

const pageOptions = computed(
  () => info.value?.pages.map((p) => ({ value: p.page, label: `Page ${p.page}` })) ?? [],
)

const needsOptions = computed(
  () => !!selected.value && ['Dropdown', 'ListBox', 'RadioGroup'].includes(selected.value.type),
)

// ---- loading -------------------------------------------------------------

watch(files, async (list) => {
  info.value = null
  fields.value = []
  result.value = null
  submitError.value = null
  loadError.value = null
  fillValues.value = {}

  if (!list[0]) return

  loading.value = true
  try {
    const inspected = await api.inspect(list[0])
    info.value = inspected
    currentPage.value = 1

    // Existing fields load onto the canvas so this edits a form rather than only adding to one.
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

  // Measured on the block-level wrapper: .designer is inline-block, so its own width
  // reports the canvas rather than the space available to it.
  const available = (surface.value?.clientWidth ?? 780) - 2
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

  // Names must be unique within a form, so a suffix is appended until one is free.
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
    options: ['Dropdown', 'ListBox', 'RadioGroup'].includes(newFieldType.value)
      ? 'Option A\nOption B'
      : '',
    toolTip: '',
  }

  fields.value.push(field)
  selectedId.value = field.id
}

function onSurfaceClick(event: MouseEvent) {
  if (!page.value || !canvas.value) return

  // Clicks landing on a field are that field's business, not the canvas's.
  if ((event.target as HTMLElement).closest('.field-box')) return

  const bounds = canvas.value.getBoundingClientRect()
  const x = (event.clientX - bounds.left) / scale.value
  const y = (event.clientY - bounds.top) / scale.value

  if (x < 0 || y < 0 || x > page.value.width || y > page.value.height) return

  addFieldAt(x, y)
}

type DragMode = 'move' | 'resize'
let drag: {
  id: number
  mode: DragMode
  startX: number
  startY: number
  originX: number
  originY: number
  originW: number
  originH: number
} | null = null

function beginDrag(event: PointerEvent, field: DraftField, dragMode: DragMode) {
  event.stopPropagation()
  event.preventDefault()

  selectedId.value = field.id
  drag = {
    id: field.id,
    mode: dragMode,
    startX: event.clientX,
    startY: event.clientY,
    originX: field.x,
    originY: field.y,
    originW: field.width,
    originH: field.height,
  }

  // Pointer capture keeps the gesture alive if the cursor leaves the element mid-drag.
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
    const produced = await api.designFields(
      files.value[0],
      {
        add: drawn.value.map((f) => ({
          name: f.name,
          type: f.type,
          rect: { page: f.page, x: f.x, y: f.y, width: f.width, height: f.height },
          required: f.required,
          readOnly: f.readOnly,
          multiline: f.multiline,
          toolTip: f.toolTip || null,
          options: optionList(f),
        })),
        // A field loaded from the document and then deleted here must go there too.
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
</script>

<template>
  <div>
    <PwPageHeader
      title="Form fields"
      description="Click the page to place a field, then drag to position and resize it. Coordinates are sent in PDF points exactly as you place them."
    >
      <template v-if="info" #actions>
        <PwSegmented v-model="mode" :options="modes" label="Form mode" />
      </template>
    </PwPageHeader>

    <PwCard title="Document" class="mb">
      <FileDrop v-model="files" />

      <template v-if="loading || loadError || info" #footer>
        <span v-if="loading" class="row t-13 muted"><PwSpinner :size="13" /> Reading document…</span>
        <span v-else-if="loadError" class="t-13" style="color: var(--bad-fg)">{{ loadError }}</span>
        <template v-else-if="info">
          <PwBadge tone="neutral">{{ info.pageCount }} page(s)</PwBadge>
          <PwBadge v-if="fields.length" tone="accent">{{ fields.length }} field(s)</PwBadge>
        </template>
      </template>
    </PwCard>

    <!-- ---- design ---- -->
    <div v-if="info && mode === 'design'" class="split">
      <PwCard title="Page" flush>
        <template #actions>
          <PwSelect
            v-model="newFieldType"
            :options="fieldTypes"
            class="type-select"
            aria-label="Field type to place"
          />
          <PwSelect
            v-if="info.pageCount > 1"
            v-model.number="currentPage"
            :options="pageOptions"
            class="page-select"
            aria-label="Page"
          />
        </template>

        <div ref="surface" class="designer-wrap">
          <div class="designer" @click="onSurfaceClick">
            <canvas ref="canvas"></canvas>

            <div
              v-for="field in pageFields"
              :key="field.id"
              class="field-box"
              :class="{ 'is-selected': field.id === selectedId, 'is-existing': field.existing }"
              :style="{
                left: `${field.x * scale}px`,
                top: `${field.y * scale}px`,
                width: `${field.width * scale}px`,
                height: `${field.height * scale}px`,
              }"
              @pointerdown="beginDrag($event, field, 'move')"
              @click.stop="selectedId = field.id"
            >
              <span class="field-box__tag">{{ field.name }}</span>
              <span class="field-box__handle" @pointerdown="beginDrag($event, field, 'resize')"></span>
            </div>
          </div>
        </div>

        <template #footer>
          <PwButton variant="solid" :loading="busy" :disabled="!fields.length" @click="applyDesign('stream')">
            Apply &amp; preview
          </PwButton>
          <PwButton :disabled="busy || !fields.length" @click="applyDesign('download')">Download</PwButton>
          <span class="t-12 subtle right">Click to add · drag to move · corner to resize</span>
        </template>
      </PwCard>

      <div class="stack-4">
        <PwCard :title="selected ? 'Selected field' : 'Fields'">
          <template v-if="selected">
            <div class="stack-4">
              <PwField v-slot="{ id }" label="Name" required>
                <PwInput :id="id" v-model="selected.name" mono />
              </PwField>

              <PwField v-slot="{ id }" label="Tooltip" help="Shown on hover in a PDF reader">
                <PwInput :id="id" v-model="selected.toolTip" />
              </PwField>

              <div class="cols-2">
                <PwField v-slot="{ id }" label="X"><PwInput :id="id" v-model="selected.x" type="number" /></PwField>
                <PwField v-slot="{ id }" label="Y"><PwInput :id="id" v-model="selected.y" type="number" /></PwField>
              </div>

              <div class="cols-2">
                <PwField v-slot="{ id }" label="Width"><PwInput :id="id" v-model="selected.width" type="number" /></PwField>
                <PwField v-slot="{ id }" label="Height"><PwInput :id="id" v-model="selected.height" type="number" /></PwField>
              </div>

              <PwField v-if="needsOptions" v-slot="{ id }" label="Options" help="One per line">
                <PwTextarea :id="id" v-model="selected.options" :rows="4" />
              </PwField>

              <div class="stack">
                <PwCheckbox v-model="selected.required" label="Required" />
                <PwCheckbox v-model="selected.readOnly" label="Read only" />
                <PwCheckbox v-if="selected.type === 'Text'" v-model="selected.multiline" label="Multiline" />
              </div>
            </div>

          </template>

          <template v-else>
            <p v-if="!fields.length" class="t-13 muted" style="margin: 0">
              No fields yet. Click anywhere on the page to place one.
            </p>

            <table v-else class="table">
              <tbody>
                <tr
                  v-for="field in fields"
                  :key="field.id"
                  class="pick"
                  @click="selectedId = field.id; currentPage = field.page"
                >
                  <td><code>{{ field.name }}</code></td>
                  <td class="t-12 subtle">{{ field.type }}</td>
                  <td class="t-12 subtle num">p{{ field.page }}</td>
                  <td>
                    <PwBadge v-if="field.existing" tone="neutral">existing</PwBadge>
                  </td>
                </tr>
              </tbody>
            </table>
          </template>

          <template v-if="selected" #footer>
            <PwButton variant="danger" size="sm" @click="removeField(selected.id)">Delete field</PwButton>
            <PwButton size="sm" @click="selectedId = null">Done</PwButton>
          </template>
        </PwCard>

        <ResultPane :result="result" :error="submitError" :busy="busy" busy-hint="Writing fields…" />
      </div>
    </div>

    <!-- ---- fill ---- -->
    <div v-else-if="info && mode === 'fill'" class="split">
      <PwCard title="Values">
        <PwCallout v-if="!info.fields.length" tone="info">
          This document has no form fields yet. Design some first, download the result, then upload
          that to fill it.
        </PwCallout>

        <div v-else class="stack-4">
          <PwField
            v-for="field in info.fields"
            :key="field.name"
            v-slot="{ id }"
            :label="field.name"
            :help="field.type"
          >
            <PwSelect
              v-if="field.options.length"
              :id="id"
              v-model="fillValues[field.name]"
              :options="[{ value: '', label: '—' }, ...field.options.map((o) => ({ value: o, label: o }))]"
            />

            <PwSelect
              v-else-if="field.type === 'Checkbox'"
              :id="id"
              v-model="fillValues[field.name]"
              :options="[
                { value: '', label: '—' },
                { value: 'true', label: 'Checked' },
                { value: 'false', label: 'Unchecked' },
              ]"
            />

            <PwInput v-else :id="id" v-model="fillValues[field.name]" :placeholder="field.value ?? ''" />
          </PwField>
        </div>

        <template #footer>
          <PwButton variant="solid" :loading="busy" :disabled="!info.fields.length" @click="fill('stream')">
            Fill &amp; preview
          </PwButton>
          <PwButton :disabled="busy || !info.fields.length" @click="fill('download')">Download</PwButton>
          <PwCheckbox v-model="flatten" label="Flatten" help="Bakes values in and removes the form" />
        </template>
      </PwCard>

      <ResultPane :result="result" :error="submitError" :busy="busy" idle-hint="The filled document appears here." />
    </div>
  </div>
</template>

<style scoped>
.mb { margin-bottom: var(--s-4); }

.type-select { width: 140px; }
.page-select { width: 110px; }

.designer-wrap {
  width: 100%;
  padding: var(--s-4);
  background: var(--bg-sunken);
  display: flex;
  justify-content: center;
}

.designer {
  position: relative;
  display: inline-block;
  line-height: 0;
  border-radius: var(--r-sm);
  overflow: hidden;
  background: #fff;
  max-width: 100%;
  cursor: crosshair;
  box-shadow: var(--shadow-md);
}

.field-box {
  position: absolute;
  border: 1.5px solid var(--a-500);
  background: color-mix(in srgb, var(--a-500) 14%, transparent);
  border-radius: 2px;
  cursor: move;
  touch-action: none;
}

.field-box.is-existing {
  border-color: var(--ok-fg);
  background: color-mix(in srgb, var(--ok-fg) 14%, transparent);
}

.field-box.is-selected {
  box-shadow: 0 0 0 2px var(--bg-raised), 0 0 0 4px var(--a-500);
  z-index: 2;
}

.field-box__tag {
  position: absolute;
  top: -18px;
  left: -1px;
  font: var(--w-semi) 10px/1.5 var(--font);
  background: var(--a-600);
  color: #fff;
  padding: 0 5px;
  border-radius: 3px;
  white-space: nowrap;
  pointer-events: none;
}

.field-box.is-existing .field-box__tag { background: var(--ok-fg); }

.field-box__handle {
  position: absolute;
  right: -5px;
  bottom: -5px;
  width: 11px;
  height: 11px;
  background: var(--bg-raised);
  border: 1.5px solid var(--a-500);
  border-radius: 2px;
  cursor: nwse-resize;
  touch-action: none;
}

.pick { cursor: pointer; }
</style>
