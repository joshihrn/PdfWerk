# PdfWerk

**Everything you need to do to a PDF — as a web app, an HTTP API, and a drop-in widget.**

Create PDFs from text or Word. Edit the text inside existing ones. Draw form fields with your
mouse, merge values in, and flatten. Combine documents. Summarise them with a free AI model.

MIT licensed, and every dependency is MIT, Apache-2.0 or BSD. No copyleft, and nothing that
changes terms once you cross a revenue threshold — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

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

Every endpoint that returns a document accepts `?delivery=download|stream|json`, so the same call
serves a browser download, an inline preview, or a base64 envelope for server-to-server use.

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
    tool: 'create',            // create | word | merge | summarize | fill | inspect
    apiKey: 'pw_…',            // optional
    delivery: 'preview',       // download | preview | callback
    onResult: (blob, meta) => console.log(meta.fileName, blob.size),
  })
</script>
```

11.7 KB (4.2 KB gzipped), no dependencies, rendered into a shadow root so the host page's CSS and
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

66 tests covering the PDF engine, Word conversion, the AI layer and the key store. They need no
network, no API key and no Docker: SQLite backs the key store and a fake provider stands in for
the model.

## Licence

MIT — see [LICENSE](LICENSE).
