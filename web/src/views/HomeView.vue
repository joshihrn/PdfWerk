<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type ActionDescriptor, type ProviderInfo } from '../api/client'

const actions = ref<ActionDescriptor[]>([])
const providers = ref<ProviderInfo[]>([])
const loadFailed = ref(false)

/** Each action maps to the tool page that drives it. */
const routeFor: Record<string, string> = {
  CreateFromText: '/create',
  CreateFromWord: '/word',
  EditText: '/edit',
  EditFormFields: '/forms',
  FillForm: '/forms',
  Merge: '/merge',
  Summarize: '/summarize',
  Inspect: '/inspect',
  Split: '/pages',
  Rotate: '/pages',
  Watermark: '/pages',
}

onMounted(async () => {
  try {
    // The catalogue is served by the API rather than duplicated here, so the landing page can
    // never drift out of step with what the server actually exposes.
    ;[actions.value, providers.value] = await Promise.all([api.actions(), api.providers()])
  } catch {
    loadFailed.value = true
  }
})
</script>

<template>
  <div>
    <section style="padding: 26px 0 8px">
      <h1>
        Everything you need to do to a PDF,<br />
        <span
          style="background: linear-gradient(90deg, var(--accent), var(--accent-2)); -webkit-background-clip: text; background-clip: text; color: transparent"
        >as a UI, an API and a drop-in widget.</span>
      </h1>

      <p class="muted" style="font-size: 17px; max-width: 64ch">
        Create PDFs from text or Word. Edit the text inside them. Draw form fields with your mouse,
        then merge values in and flatten. Combine documents, and summarise them with a free AI model.
        Every tool here is one HTTP call you can make yourself.
      </p>

      <div class="btns">
        <RouterLink class="btn primary" to="/create">Try it now</RouterLink>
        <RouterLink class="btn" to="/api">Get an API key</RouterLink>
        <a class="btn" href="/docs" target="_blank" rel="noopener">API reference ↗</a>
        <a class="btn" href="https://github.com/joshihrn/PdfWerk" target="_blank" rel="noopener">GitHub ↗</a>
      </div>
    </section>

    <div v-if="loadFailed" class="note err" style="margin-top: 24px">
      Could not reach the API. Is the server running on port 5272?
    </div>

    <h2>What it does</h2>
    <div class="grid">
      <RouterLink
        v-for="action in actions"
        :key="action.action"
        class="panel"
        :to="routeFor[action.action] ?? '/'"
        style="margin: 0; color: inherit; text-decoration: none"
      >
        <h3>
          {{ action.title }}
          <span v-if="action.requiresAi" class="tag">AI</span>
        </h3>
        <p class="muted small" style="margin-bottom: 10px">{{ action.summary }}</p>
        <code style="color: var(--accent)">POST {{ action.endpoint }}</code>
      </RouterLink>
    </div>

    <h2>Built for embedding</h2>
    <div class="split">
      <div class="panel" style="margin: 0">
        <h3>One script tag</h3>
        <p class="muted small">
          Drop the widget into any page — React, Rails, WordPress, plain HTML. It renders the tool
          you name and hands you the finished PDF as a file or a stream, whichever you ask for.
        </p>
        <pre class="out">&lt;div id="pdf"&gt;&lt;/div&gt;
&lt;script src="/pdfwerk-embed.js"&gt;&lt;/script&gt;
&lt;script&gt;
  PdfWerk.mount('#pdf', {
    tool: 'create',
    apiKey: 'pw_…',
    onResult: (blob) =&gt; { /* yours */ }
  })
&lt;/script&gt;</pre>
      </div>

      <div class="panel" style="margin: 0">
        <h3>Or just call the API</h3>
        <p class="muted small">Nothing to install. Every action is a single POST.</p>
        <pre class="out">curl -X POST \
  https://pdfwerk.com/v1/create/text \
  -H 'X-Api-Key: pw_…' \
  -H 'Content-Type: application/json' \
  -d '{"content":"# Invoice","format":"Markdown"}' \
  -o invoice.pdf</pre>
        <p class="small muted" style="margin-top: 10px; margin-bottom: 0">
          Add <code>?delivery=stream</code> to render inline instead of downloading,
          or <code>?delivery=json</code> for a base64 envelope with metadata.
        </p>
      </div>
    </div>

    <h2>Fair use, enforced</h2>
    <div class="panel">
      <p class="muted small">
        This is a public service, so every action carries its own limits — per minute, per hour and
        per day, plus caps on file size, page count and how many requests you can have in flight at
        once. Limits are reported on every response in the <code>X-RateLimit-*</code> headers, so
        you can back off before you get rejected rather than discovering the ceiling by hitting it.
      </p>

      <table>
        <thead>
          <tr><th>Tier</th><th>Most actions</th><th>Summarise</th><th>Upload</th><th>Pages</th></tr>
        </thead>
        <tbody>
          <tr><td>Anonymous</td><td>5/min · 120/day</td><td>2/min · 20/day</td><td>10 MB</td><td>50</td></tr>
          <tr><td>Free key</td><td>20/min · 1,500/day</td><td>6/min · 250/day</td><td>25 MB</td><td>300</td></tr>
          <tr><td>Pro</td><td>120/min · 30,000/day</td><td>30/min · 5,000/day</td><td>100 MB</td><td>2,000</td></tr>
        </tbody>
      </table>

      <p class="small muted" style="margin: 12px 0 0">
        A free key takes one click and no account. <RouterLink to="/api">Get one →</RouterLink>
      </p>
    </div>

    <h2>AI providers</h2>
    <div class="panel">
      <p class="muted small">
        Summarisation runs on free models, and you can pick which. Self-host with Ollama and no
        document ever leaves your machine.
      </p>

      <table>
        <thead><tr><th>Provider</th><th>Model</th><th>Context</th><th>Status</th></tr></thead>
        <tbody>
          <tr v-for="provider in providers" :key="provider.key">
            <td>{{ provider.key }}</td>
            <td><code>{{ provider.model }}</code></td>
            <td>{{ (provider.contextTokens / 1000).toFixed(0) }}k tokens</td>
            <td>
              <span class="tag" :class="{ grey: !provider.configured }">
                {{ provider.configured ? 'ready' : 'not configured' }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <h2>Open source, no surprises</h2>
    <div class="panel">
      <p class="muted small" style="margin-bottom: 0">
        MIT licensed, and every dependency is MIT, Apache-2.0 or BSD — no copyleft, no
        source-available licence, and nothing that changes terms once you pass a revenue threshold.
        Self-host the whole thing with Docker Compose, or run it with no infrastructure at all:
        without Redis or Postgres it falls back to in-process limiting and a SQLite file.
      </p>
    </div>
  </div>
</template>
