<script setup lang="ts">
/**
 * Adding text to a page, as distinct from changing text already on it.
 *
 * Its own screen rather than a mode of the editor: on the editing screen a stray click landing on
 * blank paper should do nothing, and here every click is meant to place something. Mixing the two
 * would make the safe behaviour depend on a toggle nobody can see.
 */
import { computed, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import PdfTextPicker from '../components/PdfTextPicker.vue'
import ResultPane from '../components/ResultPane.vue'
import {
  PwButton,
  PwCallout,
  PwCard,
  PwCheckbox,
  PwField,
  PwInput,
  PwPageHeader,
  PwSelect,
} from '../components/ui'
import { api, saveBlob, type Delivery, type DocumentResult } from '../api/client'

interface Placement {
  id: number
  page: number
  x: number
  y: number
  text: string
  fontSize: number
  bold: boolean
  italic: boolean
  color: string
}

const files = ref<File[]>([])
const placements = ref<Placement[]>([])
const selectedId = ref<number | null>(null)
const nextId = ref(1)

// Carried over between placements, so adding a column of entries at one size does not mean
// setting the size again for every one.
const fontSize = ref(12)
const bold = ref(false)
const italic = ref(false)
const color = ref('#111827')

const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

const selected = computed(() => placements.value.find((p) => p.id === selectedId.value) ?? null)
const ready = computed(() => files.value.length > 0 && placements.value.some((p) => p.text.trim()))

const sizes = [8, 9, 10, 11, 12, 14, 16, 18, 24, 32, 48].map((s) => ({
  value: String(s),
  label: `${s} pt`,
}))

/**
 * Ghosts for the picker.
 *
 * Placements with no text yet are still shown, so an empty box does not vanish the moment it is
 * created and leave the click looking as though it did nothing.
 */
const ghosts = computed(() =>
  placements.value.map((p) => ({
    page: p.page,
    x: p.x,
    y: p.y,
    text: p.text || 'Type here…',
    fontSize: p.fontSize,
  })),
)

function place({ page, x, y }: { page: number; x: number; y: number }) {
  const placement: Placement = {
    id: nextId.value++,
    page,
    // The click marks where the text should sit; the drawing treats y as the top of the line, so
    // the point is lifted by half a line to centre the text on the cursor rather than hang it
    // below.
    x,
    y: Math.max(0, y - fontSize.value / 2),
    text: '',
    fontSize: fontSize.value,
    bold: bold.value,
    italic: italic.value,
    color: color.value,
  }

  placements.value.push(placement)
  selectedId.value = placement.id
}

function remove(id: number) {
  placements.value = placements.value.filter((p) => p.id !== id)
  if (selectedId.value === id) selectedId.value = null
}

async function run(delivery: Delivery) {
  if (!ready.value) return

  busy.value = true
  error.value = null

  try {
    const produced = await api.annotate(
      files.value[0],
      {
        items: placements.value
          .filter((p) => p.text.trim())
          .map((p) => ({
            type: 'Text',
            page: p.page,
            x: p.x,
            y: p.y,
            text: p.text,
            fontSize: p.fontSize,
            bold: p.bold,
            italic: p.italic,
            color: p.color,
          })),
      },
      delivery,
    )

    result.value = produced
    if (delivery === 'download') saveBlob(produced.blob, produced.fileName)
  } catch (ex) {
    result.value = null
    error.value = ex
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div>
    <PwPageHeader
      title="Add text to a PDF"
      description="Click anywhere on the page and type. Unlike find and replace, this writes into blank space — a signature line, a gap in a scanned form, a note in the margin."
    />

    <div class="split">
      <div class="stack-4">
        <PwCard title="Document">
          <FileDrop v-model="files" />
        </PwCard>

        <PwCard v-if="files.length" title="Page">
          <PdfTextPicker
            :file="files[0]"
            allow-placement
            :placed="ghosts"
            @place="place"
          />

          <template #footer>
            <p class="t-12 subtle" style="margin: 0">
              The text becomes part of the page, so it prints and survives flattening — and it
              cannot be edited again once the file is saved.
            </p>
          </template>
        </PwCard>

        <PwCallout v-else tone="info" title="Choose a document to start">
          Once a PDF is loaded, clicking the page adds text where you click.
        </PwCallout>
      </div>

      <div class="stack-4">
        <PwCard title="Style" description="Applied to whatever you add next">
          <div class="cols-2">
            <PwField v-slot="{ id }" label="Size">
              <PwSelect
                :id="id"
                :model-value="String(fontSize)"
                :options="sizes"
                @update:model-value="fontSize = Number($event)"
              />
            </PwField>

            <PwField v-slot="{ id }" label="Colour">
              <PwInput :id="id" v-model="color" type="color" />
            </PwField>
          </div>

          <div class="row wrap" style="margin-top: var(--s-3)">
            <PwCheckbox v-model="bold" label="Bold" />
            <PwCheckbox v-model="italic" label="Italic" />
          </div>
        </PwCard>

        <PwCard
          v-if="placements.length"
          :title="selected ? 'Selected text' : 'Added text'"
          :description="`${placements.length} item${placements.length === 1 ? '' : 's'}`"
        >
          <div v-if="selected" class="stack-4">
            <PwField v-slot="{ id }" label="Text" help="Line breaks are kept">
              <PwInput :id="id" v-model="selected.text" placeholder="Ada Lovelace" />
            </PwField>

            <div class="cols-2">
              <PwField v-slot="{ id }" label="X"><PwInput :id="id" v-model="selected.x" type="number" /></PwField>
              <PwField v-slot="{ id }" label="Y"><PwInput :id="id" v-model="selected.y" type="number" /></PwField>
            </div>
          </div>

          <ul class="placed">
            <li v-for="item in placements" :key="item.id">
              <button
                type="button"
                class="placed__pick"
                :class="{ 'placed__pick--on': item.id === selectedId }"
                @click="selectedId = item.id"
              >
                <span class="placed__text">{{ item.text || 'Empty' }}</span>
                <span class="t-12 subtle">p{{ item.page }}</span>
              </button>

              <PwButton variant="ghost" size="sm" @click="remove(item.id)">Remove</PwButton>
            </li>
          </ul>
        </PwCard>

        <div class="row wrap">
          <PwButton variant="solid" :loading="busy" :disabled="!ready" @click="run('stream')">
            Apply &amp; preview
          </PwButton>
          <PwButton :disabled="busy || !ready" @click="run('download')">Download</PwButton>
        </div>

        <ResultPane
          :result="result"
          :error="error"
          :busy="busy"
          busy-hint="Drawing on the document…"
          idle-hint="The result appears here once you have added some text."
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.placed {
  display: flex;
  flex-direction: column;
  gap: var(--s-2);
  margin: var(--s-4) 0 0;
  padding: 0;
  list-style: none;
}

.placed li {
  display: flex;
  align-items: center;
  gap: var(--s-2);
}

.placed__pick {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--s-3);
  flex: 1;
  padding: var(--s-2) var(--s-3);
  font: inherit;
  text-align: left;
  color: var(--fg-muted);
  background: var(--bg-sunken);
  border: 1px solid var(--border);
  border-radius: var(--r-sm);
  cursor: pointer;
}

.placed__pick--on {
  color: var(--fg);
  border-color: var(--accent);
}

/* One line only: a long note would otherwise push the remove button off the row. */
.placed__text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
