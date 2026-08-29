<script setup lang="ts">
import { onMounted, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import { api, type ProviderInfo, type SummaryResult } from '../api/client'

const files = ref<File[]>([])
const style = ref('Brief')
const focus = ref('')
const provider = ref('')
const maxWords = ref(250)
const includeText = ref(false)

const providers = ref<ProviderInfo[]>([])
const summary = ref<SummaryResult | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)

onMounted(async () => {
  try {
    providers.value = await api.providers()
  } catch {
    providers.value = []
  }
})

async function run() {
  if (!files.value[0]) return

  busy.value = true
  error.value = null

  try {
    summary.value = await api.summarize(files.value[0], {
      style: style.value,
      focus: focus.value || null,
      provider: provider.value || null,
      maxWords: maxWords.value,
      includeExtractedText: includeText.value,
    })
  } catch (ex) {
    summary.value = null
    error.value = ex instanceof Error ? ex.message : String(ex)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div>
    <h1>Summarise a PDF</h1>
    <p class="muted">
      The document's text is extracted and sent to a free model. Anything too long for the model's
      context is split into parts, summarised piecewise, then merged.
    </p>

    <div
      v-if="providers.length && !providers.some((p) => p.configured)"
      class="note warn"
      style="margin-bottom: 16px"
    >
      No AI provider is configured on this server yet. Add a Gemini or Groq API key, or run Ollama
      locally, and this page will start working.
    </div>

    <div class="split">
      <div class="panel" style="margin: 0">
        <FileDrop v-model="files" />

        <div class="row" style="margin-top: 16px">
          <div>
            <label for="style">Style</label>
            <select id="style" v-model="style">
              <option>Brief</option>
              <option>Detailed</option>
              <option>Bullets</option>
              <option value="ExecutiveSummary">Executive summary</option>
            </select>
          </div>
          <div>
            <label for="words">Target length (words)</label>
            <input id="words" v-model.number="maxWords" type="number" min="40" max="2000" />
          </div>
          <div>
            <label for="provider">Provider</label>
            <select id="provider" v-model="provider">
              <option value="">Auto</option>
              <option
                v-for="p in providers"
                :key="p.key"
                :value="p.key"
                :disabled="!p.configured"
              >
                {{ p.key }}{{ p.configured ? '' : ' (not configured)' }}
              </option>
            </select>
          </div>
        </div>

        <label for="focus" style="margin-top: 12px">Focus (optional)</label>
        <input id="focus" v-model="focus" type="text" placeholder="e.g. the payment terms and any termination clauses" />

        <label style="margin-top: 12px; display: flex; align-items: center; gap: 8px; cursor: pointer">
          <input v-model="includeText" type="checkbox" style="width: auto" />
          <span>Also return the extracted text</span>
        </label>

        <div class="btns">
          <button class="btn primary" :disabled="busy || !files.length" @click="run">Summarise</button>
        </div>
      </div>

      <div class="stack">
        <div v-if="busy" class="note info"><span class="spinner"></span> Reading and summarising…</div>
        <div v-else-if="error" class="note err">{{ error }}</div>

        <template v-else-if="summary">
          <div class="note ok">
            {{ summary.providerUsed }} / {{ summary.modelUsed }} ·
            {{ summary.pageCount }} page(s) · {{ summary.wordCount.toLocaleString() }} words
          </div>

          <div class="panel" style="margin: 0">
            <h3>Summary</h3>
            <p style="margin-bottom: 0">{{ summary.summary }}</p>

            <template v-if="summary.keyPoints.length">
              <h3 style="margin-top: 18px">Key points</h3>
              <ul class="muted small" style="margin: 0; padding-left: 20px">
                <li v-for="(point, i) in summary.keyPoints" :key="i" style="margin-bottom: 5px">{{ point }}</li>
              </ul>
            </template>
          </div>

          <div v-if="summary.extractedText" class="panel" style="margin: 0">
            <h3>Extracted text</h3>
            <pre class="out" style="max-height: 320px; overflow-y: auto">{{ summary.extractedText }}</pre>
          </div>
        </template>

        <div v-else class="note info">The summary appears here.</div>
      </div>
    </div>
  </div>
</template>
