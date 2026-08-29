<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type ActionDescriptor, type ProviderInfo } from '../api/client'
import { PwBadge, PwButton, PwCallout, PwCard } from '../components/ui'

const actions = ref<ActionDescriptor[]>([])
const providers = ref<ProviderInfo[]>([])
const failed = ref(false)

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
  Protect: '/pages',
}

onMounted(async () => {
  try {
    // Served by the API rather than duplicated here, so this page cannot drift out of step
    // with what the running server actually exposes.
    ;[actions.value, providers.value] = await Promise.all([api.actions(), api.providers()])
  } catch {
    failed.value = true
  }
})

const tiers = [
  { tier: 'Anonymous', general: '5 / min', day: '120', ai: '2 / min', upload: '10 MB', pages: '50' },
  { tier: 'Free key', general: '20 / min', day: '1,500', ai: '6 / min', upload: '25 MB', pages: '300' },
  { tier: 'Pro', general: '120 / min', day: '30,000', ai: '30 / min', upload: '100 MB', pages: '2,000' },
]
</script>

<template>
  <div class="stack-6">
    <!-- Flat, factual, no gradient. What it is, who it's for, what it costs. -->
    <section class="hero">
      <PwBadge tone="neutral">Source available · BSL 1.1</PwBadge>

      <h1 class="hero__title">PDF operations as an HTTP API</h1>

      <p class="hero__lede">
        Create, edit, split, merge, fill, flatten and summarise PDFs over HTTP. Every operation is
        a single POST, available through this interface, a REST endpoint, or a widget you drop into
        your own application. Self-host it, or use the hosted service.
      </p>

      <div class="row wrap hero__actions">
        <PwButton variant="solid" size="lg" href="/create">Open the tools</PwButton>
        <PwButton size="lg" href="/api">Get an API key</PwButton>
        <PwButton variant="ghost" size="lg" href="/docs">API reference ↗</PwButton>
      </div>

      <dl class="facts">
        <div class="fact">
          <dt>Operations</dt>
          <dd>{{ actions.length || '11' }}</dd>
        </div>
        <div class="fact">
          <dt>Widget size</dt>
          <dd>4.7 <span>KB gzip</span></dd>
        </div>
        <div class="fact">
          <dt>Dependencies</dt>
          <dd>MIT <span>/ Apache</span></dd>
        </div>
        <div class="fact">
          <dt>Infrastructure</dt>
          <dd>Optional</dd>
        </div>
      </dl>
    </section>

    <PwCallout v-if="failed" tone="bad" title="API unreachable">
      Could not load the action catalogue. Is the server running on port 5272?
    </PwCallout>

    <!-- ---- operations ---- -->
    <section>
      <h2 class="section-title">Operations</h2>
      <div class="grid">
        <RouterLink
          v-for="action in actions"
          :key="action.action"
          class="op"
          :to="routeFor[action.action] ?? '/'"
        >
          <div class="op__head">
            <span class="op__title">{{ action.title }}</span>
            <PwBadge v-if="action.requiresAi" tone="accent">AI</PwBadge>
          </div>
          <p class="op__desc">{{ action.summary }}</p>
          <code class="op__endpoint">POST {{ action.endpoint }}</code>
        </RouterLink>
      </div>
    </section>

    <!-- ---- integration ---- -->
    <section>
      <h2 class="section-title">Two ways to integrate</h2>

      <div class="split split--even">
        <PwCard title="Call the API" description="Nothing to install. Every operation is one request.">
          <pre><code>curl -X POST https://pdfwerk.com/v1/create/text \
  -H 'X-Api-Key: pw_…' \
  -H 'Content-Type: application/json' \
  -d '{"content":"# Invoice","format":"Markdown"}' \
  -o invoice.pdf</code></pre>

          <p class="note-under">
            Add <code>?delivery=stream</code> to render inline, or <code>?delivery=json</code> for a
            base64 envelope with metadata.
          </p>
        </PwCard>

        <PwCard title="Embed the widget" description="One script tag. No framework, no build step.">
          <pre><code>&lt;div id="pdf"&gt;&lt;/div&gt;
&lt;script src="/pdfwerk-embed.js"&gt;&lt;/script&gt;
&lt;script&gt;
  PdfWerk.mount('#pdf', {
    tool: 'create',
    delivery: 'preview',
    onResult: (blob) =&gt; { /* yours */ }
  })
&lt;/script&gt;</code></pre>

          <p class="note-under">
            Rendered in a shadow root, so your stylesheet and the widget's cannot affect each other.
          </p>
        </PwCard>
      </div>
    </section>

    <!-- ---- limits ---- -->
    <section>
      <h2 class="section-title">Rate limits</h2>

      <PwCard flush>
        <table class="table">
          <caption class="sr-only">Rate limits by account tier</caption>
          <thead>
            <tr>
              <th scope="col">Tier</th>
              <th scope="col">Most operations</th>
              <th scope="col" class="num">Per day</th>
              <th scope="col">Summarise</th>
              <th scope="col" class="num">Upload</th>
              <th scope="col" class="num">Pages</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in tiers" :key="row.tier">
              <th scope="row" class="tier-name">{{ row.tier }}</th>
              <td>{{ row.general }}</td>
              <td class="num">{{ row.day }}</td>
              <td>{{ row.ai }}</td>
              <td class="num">{{ row.upload }}</td>
              <td class="num">{{ row.pages }}</td>
            </tr>
          </tbody>
        </table>

        <template #footer>
          <span class="t-12 muted">
            Limits are reported on every response in <code>X-RateLimit-*</code>, so you can pace
            requests rather than discovering the ceiling by hitting it.
          </span>
        </template>
      </PwCard>
    </section>

    <!-- ---- providers ---- -->
    <section>
      <h2 class="section-title">Summarisation providers</h2>

      <PwCard flush>
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Provider</th>
              <th scope="col">Model</th>
              <th scope="col" class="num">Context</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="provider in providers" :key="provider.key">
              <th scope="row" class="tier-name">{{ provider.key }}</th>
              <td><code>{{ provider.model }}</code></td>
              <td class="num">{{ (provider.contextTokens / 1000).toFixed(0) }}k</td>
              <td>
                <PwBadge :tone="provider.configured ? 'ok' : 'neutral'" :dot="provider.configured">
                  {{ provider.configured ? 'Ready' : 'Not configured' }}
                </PwBadge>
              </td>
            </tr>
          </tbody>
        </table>

        <template #footer>
          <span class="t-12 muted">
            Run Ollama locally and no document leaves your machine.
          </span>
        </template>
      </PwCard>
    </section>

    <!-- ---- licensing ---- -->
    <section>
      <h2 class="section-title">Licensing</h2>

      <div class="split split--even">
        <PwCard title="Free to self-host">
          <p class="t-13 muted" style="margin: 0">
            Run it, modify it, put it in production, use it inside a company or inside a product you
            sell. No fee and no registration. The only reservation is offering PdfWerk to third
            parties as a competing hosted service. Converts to Apache-2.0 on 29 August 2030.
          </p>
        </PwCard>

        <PwCard title="No dependency surprises">
          <p class="t-13 muted" style="margin: 0">
            Every dependency is MIT, Apache-2.0 or BSD — no copyleft, and nothing that changes terms
            above a revenue threshold. Without Redis or Postgres the service falls back to
            in-process rate limiting and a SQLite file, so it runs with no infrastructure at all.
          </p>
        </PwCard>
      </div>
    </section>
  </div>
</template>

<style scoped>
.hero {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--s-4);
  padding: var(--s-6) 0 var(--s-8);
}

.hero__title {
  font-size: var(--t-40);
  letter-spacing: -0.03em;
  line-height: 1.08;
  max-width: 18ch;
}

.hero__lede {
  font-size: var(--t-16);
  color: var(--fg-muted);
  line-height: var(--lh-snug);
  max-width: 62ch;
}

.hero__actions {
  margin-top: var(--s-1);
  gap: var(--s-2);
}

/* A short row of concrete numbers. Specifics build more confidence than adjectives. */
.facts {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: var(--s-6);
  width: 100%;
  margin-top: var(--s-6);
  padding-top: var(--s-5);
  border-top: 1px solid var(--border);
}

.fact dt {
  font-size: var(--t-11);
  text-transform: uppercase;
  letter-spacing: var(--track-wide);
  color: var(--fg-subtle);
  font-weight: var(--w-medium);
}

.fact dd {
  margin: var(--s-1) 0 0;
  font-size: var(--t-20);
  font-weight: var(--w-semi);
  letter-spacing: var(--track-tight);
}

.fact dd span {
  font-size: var(--t-13);
  font-weight: var(--w-regular);
  color: var(--fg-subtle);
}

.section-title {
  font-size: var(--t-12);
  font-weight: var(--w-semi);
  text-transform: uppercase;
  letter-spacing: var(--track-wide);
  color: var(--fg-subtle);
  margin-bottom: var(--s-3);
}

.op {
  display: flex;
  flex-direction: column;
  gap: var(--s-2);
  padding: var(--s-4);
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  color: inherit;
  transition: border-color var(--fast) var(--ease), background-color var(--fast) var(--ease);
}

.op:hover {
  border-color: var(--border-strong);
  background: var(--bg-hover);
  text-decoration: none;
}

.op__head {
  display: flex;
  align-items: center;
  gap: var(--s-2);
}

.op__title {
  font-size: var(--t-13);
  font-weight: var(--w-semi);
}

.op__desc {
  font-size: var(--t-12);
  color: var(--fg-muted);
  line-height: 1.45;
  flex: 1 1 auto;
}

.op__endpoint {
  font-size: var(--t-11);
  color: var(--fg-subtle);
  background: none;
  border: 0;
  padding: 0;
}

.tier-name {
  font-weight: var(--w-medium);
  color: var(--fg);
  text-transform: none;
  letter-spacing: normal;
  font-size: var(--t-13);
  background: none;
}

.note-under {
  margin: var(--s-3) 0 0;
  font-size: var(--t-12);
  color: var(--fg-subtle);
  line-height: 1.5;
}

@media (max-width: 720px) {
  .hero__title { font-size: var(--t-32); }
  .facts { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--s-4); }
}
</style>
