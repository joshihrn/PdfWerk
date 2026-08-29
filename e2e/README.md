# End-to-end tests

Two suites against a real, running PdfWerk:

- **`tests/api.spec.ts`** — the HTTP API on its own terms. Every operation, the error contract
  (422 for a document that cannot be read, 400 for everything else the caller got wrong, 429
  with `Retry-After` at the ceiling), tier limits, key issue and revocation, the `X-RateLimit-*`
  headers integrators depend on, and the CORS policy the embeddable widget relies on to work
  from someone else's origin.
- **`tests/ui.spec.ts`** — the browser. Written against what a person sees — headings, labels,
  button text — rather than CSS classes, so a restyle does not break the suite but a broken
  interface does. The form designer is the exception: coordinates are the thing under test.

  Beyond the feature walkthroughs it covers the embeddable widget through the demo page the
  server ships, an axe audit of every page in light and dark, keyboard operation, behaviour at
  the rate limit, error recovery, and layout at 375px.

- **`tests/demo.spec.ts`** — the guided tour. One test, one page, one continuous take through
  every feature on its happy path. The other two suites ask what happens when things go wrong;
  this one only asks whether the whole thing works end to end, and stops at the first thing that
  does not. It is the run to watch, and the one to show someone.

  It leaves evidence in `demo-output/`: a numbered screenshot per step, and the eight documents
  it produces, saved through the browser's own download path — which is also the only place
  anything exercises downloading rather than previewing. Later steps upload what earlier steps
  made, so the form it fills is the form it drew.

  ```bash
  npm run demo            # headed, watch it happen
  npm run demo:headless   # same steps, no window
  ```

  It is excluded from `npm test` on purpose: that run is for catching regressions, and the tour
  walks the same happy paths a third time.

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
