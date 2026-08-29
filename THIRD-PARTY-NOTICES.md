# Third-party notices

PdfWerk itself is under the [Business Source License 1.1](LICENSE) — see [LICENSING.md](LICENSING.md).
This page covers its **dependencies**, which are a separate question.

Every dependency PdfWerk ships is permissive — MIT, Apache-2.0, BSD or the PostgreSQL licence.
There is **no copyleft** (GPL/LGPL/AGPL) and **nothing that changes terms above a revenue
threshold**, so self-hosting carries no obligations beyond PdfWerk's own licence.

Licences below were read from the resolved package metadata, not from documentation.

## .NET

| Package | Licence | Used for |
| --- | --- | --- |
| PDFsharp 6.2.4 | MIT | Reading and writing PDFs, drawing, flattening |
| PDFsharp-MigraDoc 6.2.4 | MIT | Document layout for text, Markdown and .docx rendering |
| PdfPig 0.1.16 | Apache-2.0 | Text extraction with glyph positions, for summarising |
| DocumentFormat.OpenXml 3.5.1 | MIT | Reading .docx packages in the managed converter |
| Scalar.AspNetCore 2.17.2 | MIT | The API reference at `/docs` |
| StackExchange.Redis 2.8.58 | MIT | Distributed rate limiting |
| Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4 | PostgreSQL | Postgres provider for the key store |
| Microsoft.EntityFrameworkCore.* 9.0.11 | MIT | Key and usage storage |
| Microsoft.Extensions.* 9.0.11 | MIT | Configuration, DI, logging, HTTP clients |
| Microsoft.AspNetCore.OpenApi 9.0.14 | MIT | OpenAPI document generation |
| xunit 2.9.2, coverlet 6.0.2 | Apache-2.0 / MIT | Tests only, not shipped |

The **PostgreSQL licence** on Npgsql is a permissive BSD-style licence. It is not related to
copyleft and imposes only attribution.

## Web

| Package | Licence | Used for |
| --- | --- | --- |
| Vue 3.5 | MIT | The web UI |
| vue-router 5.3 | MIT | Client-side routing |
| pdf.js (`pdfjs-dist` 6.2) | Apache-2.0 | Rendering pages in the form designer |
| Vite 8, `@vitejs/plugin-vue` | MIT | Build tooling, not shipped |
| TypeScript | Apache-2.0 | Build tooling, not shipped |
| Storybook 10 (`@storybook/vue3-vite`, `addon-docs`, `addon-a11y`) | MIT | Design-system documentation, not shipped |
| Playwright | Apache-2.0 | End-to-end tests, not shipped |

Two development-only dependencies are MPL-2.0: **axe-core**, which Storybook's accessibility
addon runs in the browser, and **lightningcss**, which Vite uses to process CSS. Both are build
and test tooling, neither is linked into anything this project distributes, and MPL-2.0 is
file-level copyleft in any case — it would only ever apply to modifications of those files.

The embeddable widget (`pdfwerk-embed.js`) has **no runtime dependencies at all** — it is plain
TypeScript compiled to a self-contained bundle.

## External programs

**LibreOffice** (MPL-2.0) is used for high-fidelity Word conversion where it is installed. It is
invoked as a **separate process** via `soffice --convert-to`, never linked or embedded, so its
licence does not extend to this codebase. It is optional: without it, the managed OpenXML
converter handles `.docx`.

**Fonts.** PDFsharp ships no fonts. The container installs DejaVu and Liberation
(free/permissive, metric-compatible with the common Microsoft faces) so rendering works on a
minimal base image. Fonts are installed by the OS package manager, not vendored into this
repository.

## Deliberately avoided

| Library | Why not |
| --- | --- |
| iText 7 | AGPL. Would force the entire service, and anything calling it, to be AGPL. |
| QuestPDF | Nicer layout API, but dual-licensed: the "Community MIT" tier is free only below a revenue threshold. That is exactly the kind of surprise this project is meant not to have. |
| Aspose, Syncfusion, IronPDF | Commercial, per-developer or per-deployment licensing. |

## Verifying this yourself

```bash
dotnet list package --include-transitive
npm --prefix web ls --omit=dev
```

For the full tree including development tooling, with every licence resolved from the packages
themselves rather than from this document:

```bash
npx --prefix web license-checker-rseidelsohn --production=false --summary
```
