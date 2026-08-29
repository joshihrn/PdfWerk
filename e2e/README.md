# End-to-end tests

Two suites against a real, running PdfWerk:

- **`tests/api.spec.ts`** — the HTTP API on its own terms. Every operation, the error contract
  (422 for a corrupt file, 400 for a bad request, 429 with `Retry-After` at the ceiling), tier
  limits, key issue and revocation, and the `X-RateLimit-*` headers integrators depend on.
- **`tests/ui.spec.ts`** — the browser. Written against what a person sees — headings, labels,
  button text — rather than CSS classes, so a restyle does not break the suite but a broken
  interface does. The form designer is the exception: coordinates are the thing under test.

## Running

```bash
npm ci && npx playwright install chromium && npm test
```

The config starts the API itself and waits on `/health`. If one is already up on `:5272` it
attaches to that instead. Point at somewhere else with `PDFWERK_URL`:

```bash
PDFWERK_URL=https://staging.example.com npm test
```

The UI suite needs the SPA embedded in the server — run `npm run build:all` in `../web` first
if you have not already.

## Watching a run

Headless is the default so local runs and CI agree. To follow along:

```bash
npm run watch
```

That is Playwright's UI mode — a time-travel debugger where you pick tests, watch the DOM at
every step, and inspect what a selector actually matched. It is far more useful than watching a
headed browser scroll past. When the point *is* to show someone a real browser, `npm run slow`
runs headed at reduced speed with no timeout.

## Notes

- **One worker.** The service is rate limited per caller and parallel workers share one bucket,
  so concurrency produces quota failures that look like flakes.
- **No fixtures.** PDFs and `.docx` files are built at run time — the PDFs by the service
  itself, the Word file by a small stored-entry ZIP writer in `tests/support.ts`. Nothing
  binary is committed and nothing goes stale.
- **One key per run**, minted once and cached, which raises the ceiling from 5/min anonymous to
  20/min on the Free tier and keeps failures meaningful.
