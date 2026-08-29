<script setup lang="ts">
import { ref } from 'vue'
import FileDrop from '../components/FileDrop.vue'
import ResultPane from '../components/ResultPane.vue'
import { api, saveBlob, type Delivery, type DocumentResult } from '../api/client'

interface Replacement {
  find: string
  replace: string
  matchCase: boolean
  page: number | null
}

const files = ref<File[]>([])
const replacements = ref<Replacement[]>([{ find: '', replace: '', matchCase: true, page: null }])
const failOnNoMatch = ref(true)

const result = ref<DocumentResult | null>(null)
const error = ref<unknown>(null)
const busy = ref(false)

function add() {
  replacements.value.push({ find: '', replace: '', matchCase: true, page: null })
}

function remove(index: number) {
  replacements.value.splice(index, 1)
  if (replacements.value.length === 0) add()
}

async function run(delivery: Delivery) {
  if (!files.value[0]) return

  busy.value = true
  error.value = null

  try {
    const produced = await api.editText(
      files.value[0],
      {
        replacements: replacements.value
          .filter((r) => r.find.length > 0)
          .map((r) => ({ find: r.find, replace: r.replace, matchCase: r.matchCase, page: r.page })),
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
    <h1>Update text in a PDF</h1>
    <p class="muted">
      Find and replace inside an existing document. The original words are removed from the file,
      not covered over — so the old text is gone from search and copy-paste too.
    </p>

    <div class="split">
      <div class="panel" style="margin: 0">
        <FileDrop v-model="files" />

        <h3 style="margin-top: 20px">Replacements</h3>

        <div class="stack">
          <div v-for="(item, index) in replacements" :key="index" class="panel" style="margin: 0; padding: 14px">
            <div class="row">
              <div>
                <label :for="`find-${index}`">Find</label>
                <input :id="`find-${index}`" v-model="item.find" type="text" placeholder="Acme Corporation" />
              </div>
              <div>
                <label :for="`replace-${index}`">Replace with</label>
                <input :id="`replace-${index}`" v-model="item.replace" type="text" placeholder="Globex Inc" />
              </div>
            </div>

            <div class="row" style="margin-top: 10px; align-items: center">
              <label style="display: flex; align-items: center; gap: 8px; margin: 0; cursor: pointer">
                <input v-model="item.matchCase" type="checkbox" style="width: auto" />
                <span>Match case</span>
              </label>

              <div style="flex: 0 1 140px">
                <input
                  v-model.number="item.page"
                  type="number"
                  min="1"
                  placeholder="All pages"
                  title="Restrict to one page, or leave blank for the whole document"
                />
              </div>

              <div style="flex: 0 0 auto; margin-left: auto">
                <button class="btn small danger" @click="remove(index)">Remove</button>
              </div>
            </div>
          </div>
        </div>

        <div class="btns">
          <button class="btn small" @click="add">Add another</button>
        </div>

        <label style="margin-top: 16px; display: flex; align-items: center; gap: 8px; cursor: pointer">
          <input v-model="failOnNoMatch" type="checkbox" style="width: auto" />
          <span>Fail if nothing matched (rather than returning the document unchanged)</span>
        </label>

        <div class="btns">
          <button class="btn primary" :disabled="busy || !files.length" @click="run('stream')">Preview</button>
          <button class="btn" :disabled="busy || !files.length" @click="run('download')">Download</button>
        </div>
      </div>

      <div class="stack">
        <ResultPane :result="result" :error="error" :busy="busy" idle-hint="The edited document appears here." />

        <div class="panel small muted" style="margin: 0">
          <h3>When this cannot work</h3>
          <p style="margin-bottom: 0">
            Editing needs the font to carry a character map. Scanned pages are images with no text
            at all, and a few documents embed subset fonts missing the letters your replacement
            needs. In both cases you get a clear error rather than a mangled document.
          </p>
        </div>
      </div>
    </div>
  </div>
</template>
