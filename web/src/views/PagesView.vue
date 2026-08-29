<script setup lang="ts">
import { computed, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import ResultPane from '../components/ResultPane.vue'
import {
  PwBadge,
  PwButton,
  PwCard,
  PwCheckbox,
  PwField,
  PwInput,
  PwPageHeader,
  PwSegmented,
  PwSelect,
} from '../components/ui'
import { api, saveBlob, type Delivery, type DocumentResult, type PdfInfo } from '../api/client'

const mode = ref('split')
const files = ref<File[]>([])
const info = ref<PdfInfo | null>(null)

const pages = ref('all')
const splitMode = ref('Extract')
const degrees = ref(90)
const absolute = ref(false)

const text = ref('CONFIDENTIAL')
const opacity = ref(0.15)
const colour = ref('#FF0000')
const position = ref('Diagonal')
const behind = ref(false)

const userPassword = ref('')
const allowPrinting = ref(true)
const allowCopying = ref(false)
const allowModification = ref(false)

const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

const ready = computed(() => files.value.length > 0)
const pageCount = computed(() => info.value?.pageCount ?? 0)

/** A zip and an encrypted document both defeat the inline preview. */
const noPreview = computed(
  () =>
    (mode.value === 'split' && splitMode.value !== 'Extract' && pageCount.value > 1) ||
    (mode.value === 'protect' && userPassword.value.length > 0),
)

const modes = [
  { value: 'split', label: 'Split' },
  { value: 'rotate', label: 'Rotate' },
  { value: 'watermark', label: 'Watermark' },
  { value: 'protect', label: 'Protect' },
]

const splitModes = [
  { value: 'Extract', label: 'Extract — one document with the selected pages' },
  { value: 'Burst', label: 'Burst — one document per page' },
  { value: 'Groups', label: 'Groups — one per comma-separated range' },
]

const rotations = [
  { value: 90, label: '90° clockwise' },
  { value: 180, label: '180°' },
  { value: 270, label: '270° clockwise' },
  { value: -90, label: '90° anticlockwise' },
]

const positions = ['Diagonal', 'Horizontal', 'Vertical'].map((p) => ({ value: p, label: p }))

async function onFiles() {
  info.value = null
  result.value = null
  error.value = null

  if (!files.value[0]) return

  try {
    info.value = await api.inspect(files.value[0])
  } catch (ex) {
    error.value = ex
  }
}

async function run(delivery: Delivery) {
  if (!files.value[0]) return

  busy.value = true
  error.value = null

  try {
    const file = files.value[0]
    let produced: DocumentResult

    if (mode.value === 'split') {
      produced = await api.split(file, { pages: pages.value, mode: splitMode.value }, delivery)
    } else if (mode.value === 'rotate') {
      produced = await api.rotate(
        file,
        { pages: pages.value, degrees: degrees.value, absolute: absolute.value },
        delivery,
      )
    } else if (mode.value === 'watermark') {
      produced = await api.watermark(
        file,
        {
          text: text.value,
          pages: pages.value,
          opacity: opacity.value,
          color: colour.value,
          position: position.value,
          behindContent: behind.value,
        },
        delivery,
      )
    } else {
      produced = await api.protect(
        file,
        {
          userPassword: userPassword.value || null,
          permissions: {
            allowPrinting: allowPrinting.value,
            allowCopyingContent: allowCopying.value,
            allowModification: allowModification.value,
          },
        },
        delivery,
      )
    }

    result.value = produced

    // A zip always downloads, whatever was asked for.
    if (delivery === 'download' || produced.blob.type === 'application/zip') {
      saveBlob(produced.blob, produced.fileName)
    }
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
      title="Page tools"
      description="Pull out ranges, burst into single pages, rotate, watermark, or lock a document with a password."
    >
      <template #actions>
        <PwSegmented v-model="mode" :options="modes" label="Page operation" />
      </template>
    </PwPageHeader>

    <div class="split">
      <div class="stack-4">
        <PwCard title="Document">
          <FileDrop v-model="files" @update:model-value="onFiles" />

          <template v-if="info" #footer>
            <PwBadge tone="neutral">{{ info.pageCount }} page(s)</PwBadge>
            <PwBadge v-if="info.hasAcroForm" tone="accent">{{ info.fields.length }} field(s)</PwBadge>
            <PwBadge v-if="info.isEncrypted" tone="warn">encrypted</PwBadge>
          </template>
        </PwCard>

        <PwCard :title="modes.find((m) => m.value === mode)?.label">
          <div class="stack-4">
            <PwField
              v-if="mode !== 'protect'"
              v-slot="{ id }"
              label="Pages"
              help="1-3,7 · 5- · -3 · odd · even · first · last · all"
            >
              <PwInput :id="id" v-model="pages" mono placeholder="all" />
            </PwField>

            <!-- split -->
            <template v-if="mode === 'split'">
              <PwField v-slot="{ id }" label="How to split">
                <PwSelect :id="id" v-model="splitMode" :options="splitModes" />
              </PwField>
            </template>

            <!-- rotate -->
            <template v-else-if="mode === 'rotate'">
              <PwField v-slot="{ id }" label="Rotation">
                <PwSelect :id="id" v-model="degrees" :options="rotations" />
              </PwField>

              <PwCheckbox
                v-model="absolute"
                label="Replace existing rotation"
                help="Otherwise this is added to whatever the page already has"
              />
            </template>

            <!-- watermark -->
            <template v-else-if="mode === 'watermark'">
              <PwField v-slot="{ id }" label="Text">
                <PwInput :id="id" v-model="text" :maxlength="200" />
              </PwField>

              <div class="cols-2">
                <PwField v-slot="{ id }" label="Orientation">
                  <PwSelect :id="id" v-model="position" :options="positions" />
                </PwField>

                <PwField v-slot="{ id }" label="Colour">
                  <div class="row">
                    <input v-model="colour" type="color" class="swatch" aria-hidden="true" />
                    <PwInput :id="id" v-model="colour" mono placeholder="#FF0000" />
                  </div>
                </PwField>
              </div>

              <PwField v-slot="{ id }" :label="`Opacity — ${opacity.toFixed(2)}`">
                <input
                  :id="id"
                  v-model.number="opacity"
                  type="range"
                  min="0.02"
                  max="1"
                  step="0.01"
                  class="range"
                />
              </PwField>

              <PwCheckbox
                v-model="behind"
                label="Draw beneath the content"
                help="Keeps body text fully legible"
              />
            </template>

            <!-- protect -->
            <template v-else>
              <PwField
                v-slot="{ id, describedBy }"
                label="Password to open"
                help="Leave blank to set permissions only. Only a password actually encrypts."
              >
                <PwInput
                  :id="id"
                  v-model="userPassword"
                  type="password"
                  :described-by="describedBy"
                  placeholder="Optional"
                />
              </PwField>

              <div class="stack">
                <PwCheckbox v-model="allowPrinting" label="Allow printing" />
                <PwCheckbox v-model="allowCopying" label="Allow copying text" />
                <PwCheckbox v-model="allowModification" label="Allow editing" />
              </div>
            </template>
          </div>

          <template #footer>
            <PwButton
              v-if="!noPreview"
              variant="solid"
              :loading="busy"
              :disabled="!ready"
              @click="run('stream')"
            >
              Apply &amp; preview
            </PwButton>
            <PwButton
              :variant="noPreview ? 'solid' : 'outline'"
              :loading="busy && noPreview"
              :disabled="busy || !ready"
              @click="run('download')"
            >
              Download
            </PwButton>
            <span v-if="noPreview" class="t-12 subtle right">
              {{ mode === 'protect' ? 'Encrypted files cannot preview' : 'Multiple files return as a zip' }}
            </span>
          </template>
        </PwCard>
      </div>

      <ResultPane
        :result="result"
        :error="error"
        :busy="busy"
        busy-hint="Working on the document…"
        idle-hint="The result appears here."
      />
    </div>
  </div>
</template>

<style scoped>
.swatch {
  width: 34px;
  height: var(--control-h);
  padding: 2px;
  border: 1px solid var(--border-field);
  border-radius: var(--r-md);
  background: var(--bg-field);
  cursor: pointer;
  flex: none;
}

.range {
  width: 100%;
  accent-color: var(--solid-bg);
  cursor: pointer;
}
</style>
