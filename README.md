# PdfWerk

**Everything you need to do to a PDF — as a web app, an HTTP API, and a drop-in widget.**

Create PDFs from text or Word. Edit the text inside existing ones. Draw form fields with your
mouse, merge values in, and flatten. Combine documents. Summarise them with a free AI model.

Source-available under the [Business Source License 1.1](LICENSE): **free to use, modify and
self-host**, including commercially and in production. The single reservation is offering PdfWerk
to third parties as a competing hosted service. On **2030-08-29** it converts automatically to
Apache-2.0.

Every dependency is MIT, Apache-2.0 or BSD — no copyleft, nothing revenue-gated — so self-hosting
carries no licensing surprises. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## What it does

| Action | Endpoint | Notes |
| --- | --- | --- |
| Create from text | `POST /v1/create/text` | Markdown or plain text: headings, lists, tables, code, quotes, links |
| Create from Word | `POST /v1/create/word` | LibreOffice where available, managed `.docx` renderer otherwise |
| Update text | `POST /v1/edit/text` | Find and replace **inside** the content stream |
| Add/remove form fields | `POST /v1/forms/design` | Text, checkbox, radio, dropdown, list box, signature |
| Fill a form | `POST /v1/forms/fill` | Optionally flatten into non-editable page content |
| Merge | `POST /v1/merge` | Form fields carried across |
| Summarise | `POST /v1/summarize` | Gemini, Groq or a local Ollama |
| Inspect | `POST /v1/inspect` | Pages, metadata, page sizes, field inventory |
| Split | `POST /v1/split` | Extract ranges, burst into single pages, or split into groups |
| Rotate | `POST /v1/rotate` | Turn selected pages by a quarter turn |
| Watermark | `POST /v1/watermark` | Stamp text over or beneath the content |
| Protect | `POST /v1/protect` | Password to open, plus printing/copying/editing restrictions |

Every endpoint that returns a document accepts `?delivery=download|stream|json`, so the same call
serves a browser download, an inline preview, or a base64 envelope for server-to-server use.
A split that produces several documents comes back as a zip.

Page selections take the shorthand people actually write: `1-3,7`, `5-`, `-3`, `odd`, `even`,
`first`, `last`, `all`.

## Quick start

No infrastructure required — without Redis or Postgres it falls back to in-process rate limiting
and a SQLite file.

```bash
git clone https://github.com/joshihrn/PdfWerk.git
cd PdfWerk
npm --prefix web ci && npm --prefix web run build:all
dotnet run --project src/PdfWerk.Api
```

Then open <http://localhost:5272>. The API reference is at `/docs`, the embed examples at
`/embed-demo.html`.

For UI work, run the Vite dev server instead — it proxies the API, so hot reload works:

```bash
npm --prefix web run dev     # http://localhost:5173
```

### With Docker

```bash
cp .env.example .env     # set POSTGRES_PASSWORD and ADDRESS_SALT
docker compose up --build
```

That brings up the API with Postgres and Redis, and installs LibreOffice and the fonts PDFsharp
needs on Linux.

## Using the API

```bash
curl -X POST http://localhost:5272/v1/create/text \
  -H 'Content-Type: application/json' \
  -d '{"content":"# Invoice\n\nBilled to **Acme**.","format":"Markdown","title":"Invoice"}' \
  -o invoice.pdf
```

A free API key raises your limits and takes one call — no account, no email:

```bash
curl -X POST http://localhost:5272/v1/keys \
  -H 'Content-Type: application/json' -d '{"label":"my integration"}'
```

Then send it as `X-Api-Key: pw_…` or `Authorization: Bearer pw_…`. The secret is shown once and
stored only as a hash, so keep it.

## Embedding

```html
<div id="pdf"></div>
<script src="https://your-host/pdfwerk-embed.js"></script>
<script>
  PdfWerk.mount('#pdf', {
    tool: 'create',            // create | word | merge | summarize | fill
                               // | inspect | split | rotate | watermark
    apiKey: 'pw_…',            // optional
    delivery: 'preview',       // download | preview | callback
    onResult: (blob, meta) => console.log(meta.fileName, blob.size),
  })
</script>
```

14 KB (4.7 KB gzipped), no dependencies, rendered into a shadow root so the host page's CSS and
the widget's cannot reach each other. `baseUrl` defaults to whichever origin served the script, so
cross-origin embedding needs no configuration.

## Rate limiting

This is built to be exposed publicly, so limits are enforced per action rather than globally, and
are reported on every response in `X-RateLimit-Limit`, `X-RateLimit-Remaining` and
`X-RateLimit-Reset` — you can pace yourself instead of discovering the ceiling by hitting it.

| Tier | Most actions | Summarise | Upload | Pages | Concurrent |
| --- | --- | --- | --- | --- | --- |
| Anonymous | 5/min · 120/day | 2/min · 20/day | 10 MB | 50 | 2 |
| Free key | 20/min · 1,500/day | 6/min · 250/day | 25 MB | 300 | 4 |
| Pro | 120/min · 30,000/day | 30/min · 5,000/day | 100 MB | 2,000 | 16 |

Every number is configurable under `RateLimits` in `appsettings.json`. Alongside the counters
there are hard guards on upload size, page count, batch size and text length, all checked before
any work begins.

`GET /v1/quota` reports what you have left without consuming anything.

> **Running more than one instance?** Set `ConnectionStrings:Redis`. The in-process limiter counts
> per process, so two instances behind a load balancer each enforce the quota separately and the
> effective limit doubles. The app logs a warning at startup if it detects this outside development.

## AI providers

Summarisation is the only feature needing a model, and it works with free tiers:

| Provider | Get a key | Notes |
| --- | --- | --- |
| Gemini *(default)* | [aistudio.google.com](https://aistudio.google.com/apikey) | Large context, so most documents need no chunking |
| Groq | [console.groq.com](https://console.groq.com/keys) | Much faster, smaller context |
| Ollama | — | Fully local; no document leaves your machine |

```bash
dotnet user-secrets --project src/PdfWerk.Api set "Ai:Gemini:ApiKey" "YOUR_KEY"
```

Documents longer than the model's context are split, summarised piecewise and merged. Document
text is fenced and declared untrusted in the system prompt, because a PDF can carry text
engineered to read as instructions.

## Architecture

```
src/
  PdfWerk.Core/            Abstractions, models, rate-limit policy — no dependencies
  PdfWerk.Pdf/             PDFsharp + PdfPig engine: compose, edit, forms, merge, extract
  PdfWerk.Ai/              Pluggable providers and the summarizer
  PdfWerk.Infrastructure/  Redis limiter, EF Core key store
  PdfWerk.Api/             Minimal API endpoints, quota enforcement, OpenAPI
web/
  src/                     Vue 3 UI, including the pdf.js form designer
  embed/                   The dependency-free embeddable widget
```

Three implementation notes that are not obvious from the code:

**PDFsharp cannot create form fields.** It reads and fills them but exposes no authoring API, so
the field and widget dictionaries are built directly against ISO 32000-1 §12.7.

**Text editing rewrites the content stream** rather than covering old text with a white box. A
covered replacement still shows up in search and copy-paste, which is wrong for an edit and a
disclosure risk for anyone using it to strip sensitive text. Where a font carries no character
map — scanned pages especially — it reports that rather than faking it.

**The designer works in PDF points with a top-left origin**, the same space the API accepts, so
the only transform between mouse and document is the display scale.

## Testing

```bash
dotnet test
```

165 tests covering the PDF engine, Word conversion, page operations, the AI layer and the key
store — including a hardening suite that feeds the endpoints malformed files, PDF syntax inside
replacement text, hostile field names and degenerate geometry. They need no network, no API key
and no Docker: SQLite backs the key store and a fake provider stands in for the model.

### End to end

```bash
cd e2e && npm ci && npx playwright install chromium && npm test
```

164 Playwright tests against a real running instance — 45 driving the HTTP API directly and 119
driving the browser, including an axe accessibility audit of every page in both themes. They start the server themselves (or attach to one already on `:5272`),
mint their own API key, and build their own PDF and `.docx` fixtures through the service, so
there are no binary files checked in and nothing to set up first.

| Command | What it does |
| --- | --- |
| `npm test` | Headless, both projects. What CI runs. |
| `npm run watch` | Playwright UI mode: pick tests, watch them run, step back through any point in time |
| `npm run slow` | Headed and slowed down, for watching a flow in a real browser |
| `npm run api` / `npm run ui` | One project only |
| `npm run demo` | The guided tour: every feature in one continuous, narrated run, paced to be watched |
| `npm run report` | Opens the last HTML report |

They run on a single worker on purpose. The service is rate limited per caller and parallel
workers share one bucket, so concurrency makes tests fail on each other's quota instead of on
their own behaviour — and those failures read as flakes. Traces, video and screenshots are kept
for failures only.

## Design system

```bash
npm --prefix web run storybook
```

Storybook on <http://localhost:6006>, covering the twelve UI primitives and the tokens they are
built from: the neutral ramp, the accent, status colours, the type scale, spacing, elevation and
control sizing. The token stories read their values live out of the document rather than
restating them, so they cannot drift from `tokens.css`.

The theme switcher in the toolbar sets `data-theme` on `<html>`, the same way the application
does, so any component can be checked in light and dark without leaving the story. The
accessibility addon runs axe against each one — worth having here because these components are
meant to be embedded in other people's applications, where an inaccessible control becomes their
problem rather than ours.

Stories cover the primitives only. The views that compose them are covered end to end by
Playwright against a real API, which proves more than a mocked view in Storybook would.

## Search visibility

The UI renders in the browser, which means a crawler that does not run JavaScript sees an empty
container and one title shared by all ten routes. Google executes JavaScript; Bing is uneven at
it, and no social scraper does — so a link to `/merge` posted anywhere would have previewed as
the generic site description.

So the shell is rewritten per route before it is served: title, description, canonical, Open
Graph, Twitter card and a schema.org block, plus a short crawlable summary carrying the page's
own heading, its own introduction, and links to the other tools. That summary is a faithful
precis of the page rather than extra keywords, and Vue discards it the moment it mounts.

`robots.txt` and `sitemap.xml` are generated from the same catalogue the pages use, so a new
route cannot appear without the sitemap knowing about it. The API is disallowed: crawling the
operation endpoints would spend a caller's quota and index a stream of bytes.

Page metadata lives in one file, [`web/public/seo.json`](web/public/seo.json). The build copies
it into `wwwroot` for the server and bundles it into the app for client-side navigation, so both
answer from the same source. A test asserts the two agree on every title.

Set the canonical origin with `Seo:BaseUrl` (default `https://pdfwerk.com`). The browser derives
its own canonical from the live origin instead, so a staging deployment does not claim
production URLs as its own.

The Open Graph card is regenerated with:

```bash
node web/tools/make-og-image.mjs
```

## Deploying

See [DEPLOYMENT.md](DEPLOYMENT.md) for DNS, TLS, and the two settings that decide whether your
rate limits are real: Redis, and whether forwarded headers should be trusted.

## Licence

[Business Source License 1.1](LICENSE) — free to use, modify and self-host, including
commercially. The only reservation is offering PdfWerk to third parties as a competing hosted
service. Converts to [Apache-2.0](LICENSES/Apache-2.0.txt) on 2030-08-29.

[LICENSING.md](LICENSING.md) explains what you may and may not do, with worked examples.

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). They require agreement to the
[CLA](CLA.md), which exists so the project can honour its own Change Date; you keep full ownership
of your work.

Security issues: **please report privately**, see [SECURITY.md](SECURITY.md).
