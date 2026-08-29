<script setup lang="ts">
import { computed, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import ResultPane from '../components/ResultPane.vue'
import { api, saveBlob, type Delivery, type DocumentResult, type PdfInfo } from '../api/client'

type Mode = 'split' | 'rotate' | 'watermark'

const mode = ref<Mode>('split')
const files = ref<File[]>([])
const info = ref<PdfInfo | null>(null)

const pages = ref('all')
const splitMode = ref<'Extract' | 'Burst' | 'Groups'>('Extract')
const degrees = ref(90)
const absolute = ref(false)

const text = ref('CONFIDENTIAL')
const opacity = ref(0.15)
const colour = ref('#FF0000')
const position = ref<'Diagonal' | 'Horizontal' | 'Vertical'>('Diagonal')
const behind = ref(false)

const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

const pageCount = computed(() => info.value?.pageCount ?? 0)

// A zip cannot be previewed, so the preview button is hidden when one is expected.
const producesArchive = computed(
  () => mode.value === 'split' && splitMode.value !== 'Extract' && pageCount.value > 1,
)

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
      produced = await api.rotate(file, { pages: pages.value, degrees: degrees.value, absolute: absolute.value }, delivery)
    } else {
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
    }

    result.value = produced

    // A zip always comes back as a download regardless of what was asked for.
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
    <h1>Page tools</h1>
    <p class="muted">
      Pull out ranges, burst into single pages, rotate, or stamp a watermark. Page selections take
      the usual shorthand: <code>1-3,7</code>, <code>5-</code>, <code>odd</code>, <code>all</code>.
    </p>

    <div class="panel">
      <FileDrop v-model="files" @update:model-value="onFiles" />
      <p v-if="info" class="small muted" style="margin: 10px 0 0">
        {{ info.pageCount }} page(s) loaded.
      </p>
    </div>

    <div class="btns" style="margin-bottom: 14px">
      <button class="btn" :class="{ primary: mode === 'split' }" @click="mode = 'split'">Split</button>
      <button class="btn" :class="{ primary: mode === 'rotate' }" @click="mode = 'rotate'">Rotate</button>
      <button class="btn" :class="{ primary: mode === 'watermark' }" @click="mode = 'watermark'">Watermark</button>
    </div>

    <div class="split">
      <div class="panel" style="margin: 0">
        <label for="pages">Pages</label>
        <input id="pages" v-model="pages" type="text" placeholder="all, 1-3,7, odd, 5-" />

        <template v-if="mode === 'split'">
          <label for="splitmode" style="margin-top: 12px">How to split</label>
          <select id="splitmode" v-model="splitMode">
            <option value="Extract">Extract — one document with the selected pages</option>
            <option value="Burst">Burst — one document per page</option>
            <option value="Groups">Groups — one document per comma-separated range</option>
          </select>
          <p v-if="producesArchive" class="small muted" style="margin-top: 10px">
            Several documents are returned together as a zip.
          </p>
        </template>

        <template v-else-if="mode === 'rotate'">
          <label for="degrees" style="margin-top: 12px">Rotation</label>
          <select id="degrees" v-model.number="degrees">
            <option :value="90">90° clockwise</option>
            <option :value="180">180°</option>
            <option :value="270">270° clockwise</option>
            <option :value="-90">90° anticlockwise</option>
          </select>

          <label style="margin-top: 12px; display: flex; align-items: center; gap: 8px; cursor: pointer">
            <input v-model="absolute" type="checkbox" style="width: auto" />
            <span>Replace any existing rotation instead of adding to it</span>
          </label>
        </template>

        <template v-else>
          <label for="wtext" style="margin-top: 12px">Text</label>
          <input id="wtext" v-model="text" type="text" maxlength="200" />

          <div class="row" style="margin-top: 12px">
            <div>
              <label for="wpos">Orientation</label>
              <select id="wpos" v-model="position">
                <option>Diagonal</option><option>Horizontal</option><option>Vertical</option>
              </select>
            </div>
            <div>
              <label for="wcol">Colour</label>
              <input id="wcol" v-model="colour" type="text" placeholder="#FF0000" />
            </div>
            <div>
              <label for="wop">Opacity — {{ opacity.toFixed(2) }}</label>
              <input id="wop" v-model.number="opacity" type="range" min="0.02" max="1" step="0.01" />
            </div>
          </div>

          <label style="margin-top: 12px; display: flex; align-items: center; gap: 8px; cursor: pointer">
            <input v-model="behind" type="checkbox" style="width: auto" />
            <span>Draw beneath the content, so body text stays fully legible</span>
          </label>
        </template>

        <div class="btns">
          <button v-if="!producesArchive" class="btn primary" :disabled="busy || !files.length" @click="run('stream')">
            Preview
          </button>
          <button class="btn" :class="{ primary: producesArchive }" :disabled="busy || !files.length" @click="run('download')">
            Download
          </button>
        </div>
      </div>

      <ResultPane
        :result="result"
        :error="error"
        :busy="busy"
        idle-hint="The result appears here."
      />
    </div>
  </div>
</template>
