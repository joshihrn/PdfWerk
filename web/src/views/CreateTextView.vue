<script setup lang="ts">
import { computed, ref } from 'vue'
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
  PwTextarea,
} from '../components/ui'
import { api, saveBlob, type Delivery, type DocumentResult } from '../api/client'

const content = ref(`# Service Agreement

This agreement is made between **Acme Corporation** and the client.

## Terms

1. Payment is due within 30 days of invoice.
2. Either party may terminate with 60 days written notice.
3. All work product remains the property of the client.

| Item | Amount |
| --- | ---: |
| Setup | 1,200 |
| Monthly retainer | 350 |

> Signed on behalf of both parties.

See the [full terms](https://example.com/terms) for details.
`)

const title = ref('Service Agreement')
const author = ref('')
const format = ref('Markdown')
const page = ref('A4')
const orientation = ref('Portrait')
const marginMm = ref(20)
const fontSize = ref(11)
const pageNumbers = ref(true)

const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

/*
 * Drafting writes into the same editor the Preview button reads from, rather than being a mode
 * of its own. A draft is a starting point, and the thing people most want to do with one is
 * change it before it becomes a document — so it lands where the text already is.
 */
const brief = ref('')
const drafting = ref(false)
const draftError = ref<string | null>(null)
const draftedBy = ref<string | null>(null)

const canDraft = computed(() => brief.value.trim().length >= 10 && !drafting.value)

async function draft() {
  if (!canDraft.value) return

  drafting.value = true
  draftError.value = null

  try {
    const drafted = await api.draftDocument({
      brief: brief.value,
      title: title.value || null,
    })

    content.value = drafted.content
    draftedBy.value = drafted.model
    brief.value = ''
  } catch (ex) {
    // Shown beside the brief rather than in the result pane: the result pane is about the
    // rendered PDF, and nothing has been rendered yet.
    draftError.value = ex instanceof Error ? ex.message : String(ex)
  } finally {
    drafting.value = false
  }
}

const empty = computed(() => content.value.trim().length === 0)

const formats = [
  { value: 'Markdown', label: 'Markdown' },
  { value: 'Plain', label: 'Plain text' },
]

const pages = ['A4', 'Letter', 'Legal', 'A3', 'A5'].map((p) => ({ value: p, label: p }))

const orientations = [
  { value: 'Portrait', label: 'Portrait' },
  { value: 'Landscape', label: 'Landscape' },
]

async function run(delivery: Delivery) {
  busy.value = true
  error.value = null

  try {
    const produced = await api.createFromText(
      {
        content: content.value,
        title: title.value || null,
        author: author.value || null,
        format: format.value,
        page: page.value,
        orientation: orientation.value,
        marginMm: marginMm.value,
        fontSize: fontSize.value,
        pageNumbers: pageNumbers.value,
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
      title="Create a PDF from text"
      description="Write Markdown or plain text and get a clean, paginated document. Headings, lists, tables, block quotes, code and links are all rendered."
    />

    <PwCard title="Describe it and have it written" class="draft">
      <div class="stack-4">
        <PwCallout v-if="draftError" tone="bad" assertive title="That draft did not come back">
          {{ draftError }}
        </PwCallout>

        <PwField
          v-slot="{ id }"
          label="What should the document say?"
          help="A sentence or two is enough. The draft replaces the body below, where you can edit it before rendering."
        >
          <PwTextarea
            :id="id"
            v-model="brief"
            :rows="3"
            :mono="false"
            placeholder="A two-page service agreement between Acme Ltd and a freelance designer, covering payment terms, ownership of work and a 60-day notice period."
          />
        </PwField>
      </div>

      <template #footer>
        <PwButton :loading="drafting" :disabled="!canDraft" @click="draft">
          Write it for me
        </PwButton>
        <span v-if="drafting" class="t-12 subtle">This takes a few seconds…</span>
        <span v-else-if="draftedBy" class="t-12 subtle">Drafted by {{ draftedBy }}. Edit it below.</span>
        <span v-else class="t-12 subtle">Optional — you can write the body yourself instead</span>
      </template>
    </PwCard>

    <div class="split">
      <PwCard title="Content">
        <div class="stack-4">
          <PwField v-slot="{ id }" label="Document body" hide-label>
            <PwTextarea :id="id" v-model="content" :rows="18" placeholder="# Heading…" />
          </PwField>

          <div class="cols-2">
            <PwField v-slot="{ id }" label="Title" help="Used for the heading and the file name">
              <PwInput :id="id" v-model="title" placeholder="Untitled document" />
            </PwField>

            <PwField v-slot="{ id }" label="Author" help="Optional byline under the title">
              <PwInput :id="id" v-model="author" placeholder="—" />
            </PwField>
          </div>
        </div>

        <template #footer>
          <PwButton variant="solid" :loading="busy" :disabled="empty" @click="run('stream')">
            Preview
          </PwButton>
          <PwButton :disabled="busy || empty" @click="run('download')">Download</PwButton>
          <span v-if="empty" class="t-12 subtle right">Add some content first</span>
        </template>
      </PwCard>

      <div class="stack-4">
        <PwCard title="Layout">
          <div class="stack-4">
            <PwField v-slot="{ id }" label="Format">
              <PwSelect :id="id" v-model="format" :options="formats" />
            </PwField>

            <div class="cols-2">
              <PwField v-slot="{ id }" label="Page size">
                <PwSelect :id="id" v-model="page" :options="pages" />
              </PwField>

              <PwField v-slot="{ id }" label="Orientation">
                <PwSelect :id="id" v-model="orientation" :options="orientations" />
              </PwField>
            </div>

            <div class="cols-2">
              <PwField v-slot="{ id }" label="Margin (mm)">
                <PwInput :id="id" v-model="marginMm" type="number" :min="0" :max="100" />
              </PwField>

              <PwField v-slot="{ id }" label="Font size (pt)">
                <PwInput :id="id" v-model="fontSize" type="number" :min="5" :max="48" />
              </PwField>
            </div>

            <PwCheckbox v-model="pageNumbers" label="Page numbers" help="Adds a “Page N of M” footer" />
          </div>
        </PwCard>

        <ResultPane
          :result="result"
          :error="error"
          :busy="busy"
          busy-hint="Rendering the document…"
          idle-hint="Preview renders the PDF here without downloading it."
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.draft { margin-bottom: var(--s-5); }
</style>
