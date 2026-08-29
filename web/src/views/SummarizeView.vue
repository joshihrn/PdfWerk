<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import {
  PwBadge,
  PwButton,
  PwCallout,
  PwCard,
  PwCheckbox,
  PwField,
  PwInput,
  PwPageHeader,
  PwSelect,
  PwSpinner,
} from '../components/ui'
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

const ready = computed(() => files.value.length > 0)
const anyConfigured = computed(() => providers.value.some((p) => p.configured))

const styles = [
  { value: 'Brief', label: 'Brief — two or three sentences' },
  { value: 'Detailed', label: 'Detailed — covers each section' },
  { value: 'Bullets', label: 'Bullets — substance in the key points' },
  { value: 'ExecutiveSummary', label: 'Executive — outcomes, risks and money' },
]

const providerOptions = computed(() => [
  { value: '', label: 'Auto — first available' },
  ...providers.value.map((p) => ({
    value: p.key,
    label: p.configured ? p.key : `${p.key} (not configured)`,
    disabled: !p.configured,
  })),
])

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
    <PwPageHeader
      title="Summarise a PDF"
      description="The document's text is extracted and sent to a model. Anything longer than the model's context is split into parts, summarised piecewise, then merged."
    />

    <PwCallout
      v-if="providers.length && !anyConfigured"
      tone="warn"
      title="No AI provider configured"
      class="mb"
    >
      Add a Gemini or Groq API key, or run Ollama locally, and this page starts working. Every
      other tool is unaffected.
    </PwCallout>

    <div class="split">
      <PwCard title="Document">
        <div class="stack-4">
          <FileDrop v-model="files" />

          <div class="cols-2">
            <PwField v-slot="{ id }" label="Style">
              <PwSelect :id="id" v-model="style" :options="styles" />
            </PwField>

            <PwField v-slot="{ id }" label="Provider">
              <PwSelect :id="id" v-model="provider" :options="providerOptions" />
            </PwField>
          </div>

          <PwField
            v-slot="{ id }"
            label="Focus"
            help="Optional. Steers the summary toward what you care about."
          >
            <PwInput
              :id="id"
              v-model="focus"
              placeholder="e.g. the payment terms and any termination clauses"
            />
          </PwField>

          <div class="cols-2">
            <PwField v-slot="{ id }" label="Target length (words)">
              <PwInput :id="id" v-model="maxWords" type="number" :min="40" :max="2000" />
            </PwField>
          </div>

          <PwCheckbox v-model="includeText" label="Also return the extracted text" />
        </div>

        <template #footer>
          <PwButton variant="solid" :loading="busy" :disabled="!ready" @click="run">
            Summarise
          </PwButton>
          <span v-if="busy" class="t-12 subtle">Free tiers can take a few seconds</span>
        </template>
      </PwCard>

      <div class="stack-4">
        <PwCallout v-if="busy" tone="info">
          <span class="row"><PwSpinner :size="13" /> Extracting text and summarising…</span>
        </PwCallout>

        <PwCallout v-else-if="error" tone="bad" title="Could not summarise">{{ error }}</PwCallout>

        <template v-else-if="summary">
          <PwCard title="Summary">
            <template #actions>
              <PwBadge tone="neutral" mono>{{ summary.modelUsed }}</PwBadge>
            </template>

            <p class="lede">{{ summary.summary }}</p>

            <template v-if="summary.keyPoints.length">
              <h4 class="points__title">Key points</h4>
              <ul class="points">
                <li v-for="(point, i) in summary.keyPoints" :key="i">{{ point }}</li>
              </ul>
            </template>

            <template #footer>
              <span class="t-12 subtle">
                {{ summary.providerUsed }} · {{ summary.pageCount }} page(s) ·
                {{ summary.wordCount.toLocaleString() }} words read
              </span>
            </template>
          </PwCard>

          <PwCard v-if="summary.extractedText" title="Extracted text" flush>
            <pre class="extract">{{ summary.extractedText }}</pre>
          </PwCard>
        </template>

        <PwCallout v-else tone="info">The summary appears here.</PwCallout>
      </div>
    </div>
  </div>
</template>

<style scoped>
.mb { margin-bottom: var(--s-4); }

.lede {
  margin: 0;
  font-size: var(--t-14);
  line-height: var(--lh-base);
}

.points__title {
  margin: var(--s-5) 0 var(--s-2);
  font-size: var(--t-11);
  text-transform: uppercase;
  letter-spacing: var(--track-wide);
  color: var(--fg-subtle);
}

.points {
  margin: 0;
  padding-left: var(--s-4);
  display: flex;
  flex-direction: column;
  gap: var(--s-2);
  font-size: var(--t-13);
  color: var(--fg-muted);
}

.extract {
  max-height: 360px;
  overflow: auto;
  margin: 0;
  border: 0;
  border-radius: 0;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
