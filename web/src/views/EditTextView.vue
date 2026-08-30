<script setup lang="ts">
import { computed, ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import PdfTextPicker from '../components/PdfTextPicker.vue'
import ResultPane from '../components/ResultPane.vue'
import {
  PwButton,
  PwCard,
  PwCheckbox,
  PwField,
  PwInput,
  PwPageHeader,
} from '../components/ui'
import { api, saveBlob, type Delivery, type DocumentResult } from '../api/client'

interface Replacement {
  find: string
  replace: string
  matchCase: boolean
  page: number | null
  /** Came from clicking the page rather than being typed into the list. */
  picked?: boolean
}

const files = ref<File[]>([])
const replacements = ref<Replacement[]>([{ find: '', replace: '', matchCase: true, page: null }])
const failOnNoMatch = ref(true)

const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

const usable = computed(() => replacements.value.filter((r) => r.find.length > 0))
const ready = computed(() => files.value.length > 0 && usable.value.length > 0)

function add() {
  replacements.value.push({ find: '', replace: '', matchCase: true, page: null })
}

/**
 * Every replacement that came from clicking the page, keyed by the text it replaces.
 *
 * Lets the picker mark a run as already changed, and lets a second click on the same word edit
 * the existing rule rather than stack another one on top of it.
 */
const pickedRuns = computed(() =>
  Object.fromEntries(
    replacements.value.filter((r) => r.picked && r.find).map((r) => [r.find, r.replace]),
  ),
)

function applyPick({ find, replace, page }: { find: string; replace: string; page: number }) {
  const existing = replacements.value.find((r) => r.find === find && r.page === page)

  if (existing) {
    existing.replace = replace
    return
  }

  // Scoped to the page it was clicked on. The same word elsewhere in the document is a different
  // occurrence, and rewriting all of them is not what clicking one of them asked for.
  const rule: Replacement = { find, replace, matchCase: true, page, picked: true }

  // Reuse the blank starter row rather than leaving it stranded above the real rules.
  const blank = replacements.value.findIndex((r) => !r.find && !r.replace)
  if (blank >= 0) replacements.value.splice(blank, 1, rule)
  else replacements.value.push(rule)
}

function remove(index: number) {
  replacements.value.splice(index, 1)
  if (replacements.value.length === 0) add()
}

async function run(delivery: Delivery) {
  if (!ready.value) return

  busy.value = true
  error.value = null

  try {
    const produced = await api.editText(
      files.value[0],
      {
        replacements: usable.value.map((r) => ({
          find: r.find,
          replace: r.replace,
          matchCase: r.matchCase,
          page: r.page,
        })),
        failOnNoMatch: failOnNoMatch.value,
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
      title="Update text in a PDF"
      description="Find and replace inside an existing document. The original words are removed from the file rather than covered over, so the old text is gone from search and copy-paste too."
    />

    <div class="split">
      <div class="stack-4">
        <PwCard title="Document">
          <FileDrop v-model="files" />
        </PwCard>

        <PwCard
          v-if="files.length"
          title="Click a word to change it"
          description="Edits are applied by replacing that exact text on that page"
        >
          <PdfTextPicker :file="files[0]" :edited-runs="pickedRuns" @pick="applyPick" />

          <template #footer>
            <p class="t-12 subtle" style="margin: 0">
              The surrounding text does not reflow, so a much longer replacement can overlap what
              follows it. Anything you change here appears in the list below, where it can be
              adjusted or removed.
            </p>
          </template>
        </PwCard>

        <PwCard title="Replacements" :description="`${usable.length} of ${replacements.length} ready`">
          <template #actions>
            <PwButton size="sm" @click="add">Add another</PwButton>
          </template>

          <div class="stack-4">
            <div v-for="(item, index) in replacements" :key="index" class="rule">
              <div class="cols-2">
                <PwField v-slot="{ id }" label="Find">
                  <PwInput :id="id" v-model="item.find" placeholder="Acme Corporation" />
                </PwField>

                <PwField v-slot="{ id }" label="Replace with">
                  <PwInput :id="id" v-model="item.replace" placeholder="Globex Inc" />
                </PwField>
              </div>

              <div class="rule__opts">
                <PwCheckbox v-model="item.matchCase" label="Match case" />

                <PwField v-slot="{ id }" label="Page" hide-label>
                  <PwInput
                    :id="id"
                    v-model="item.page"
                    type="number"
                    :min="1"
                    placeholder="All pages"
                  />
                </PwField>

                <PwButton
                  variant="ghost"
                  size="sm"
                  :disabled="replacements.length === 1 && !item.find"
                  @click="remove(index)"
                >
                  Remove
                </PwButton>
              </div>
            </div>
          </div>

          <template #footer>
            <PwCheckbox
              v-model="failOnNoMatch"
              label="Fail if nothing matched"
              help="Otherwise the document comes back unchanged"
            />
          </template>
        </PwCard>

        <div class="row wrap">
          <PwButton variant="solid" :loading="busy" :disabled="!ready" @click="run('stream')">
            Apply &amp; preview
          </PwButton>
          <PwButton :disabled="busy || !ready" @click="run('download')">Download</PwButton>
        </div>
      </div>

      <div class="stack-4">
        <ResultPane
          :result="result"
          :error="error"
          :busy="busy"
          busy-hint="Rewriting the document…"
          idle-hint="The edited document appears here."
        />

        <PwCard title="When this cannot work">
          <p class="t-13 muted" style="margin: 0">
            Editing needs the font to carry a character map. Scanned pages are images with no text
            at all, and some documents embed subset fonts missing the letters a replacement needs.
            In both cases you get a clear error rather than a mangled document.
          </p>
        </PwCard>
      </div>
    </div>
  </div>
</template>

<style scoped>
.rule {
  display: flex;
  flex-direction: column;
  gap: var(--s-3);
  padding: var(--s-3);
  background: var(--bg-sunken);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
}

.rule__opts {
  display: flex;
  align-items: center;
  gap: var(--s-4);
  flex-wrap: wrap;
}

/* The page field is a narrow numeric input; letting it stretch would imply free text. */
.rule__opts > :nth-child(2) {
  width: 120px;
  flex: none;
}

.rule__opts > :last-child {
  margin-left: auto;
}
</style>
