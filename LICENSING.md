# Licensing, in plain terms

PdfWerk is under the [Business Source License 1.1](LICENSE). This page explains what that means
without the legalese. **The [LICENSE](LICENSE) file is what actually governs** — where this page
and that file disagree, the licence wins.

## The short version

**Use it for almost anything, free.** Run it, modify it, self-host it, put it in production, use it
inside your company, build it into a product you sell. No fee, no registration, no limits.

**One thing is reserved:** you may not offer PdfWerk to third parties as a hosted or managed
service that gives them access to a substantial part of its functionality. In other words, don't
run a competing PDF-as-a-service business on this code.

**It becomes Apache-2.0 on 29 August 2030.** That is written into the licence and cannot be
revoked. Every version released carries its own four-year clock.

## Concrete examples

| What you want to do | Allowed? |
| --- | --- |
| Run it on your own server to process your company's documents | ✅ Yes |
| Modify it and run your modified version internally | ✅ Yes |
| Build it into your SaaS as a background component that generates your users' invoices | ✅ Yes |
| Ship it inside a desktop or on-premise product you sell | ✅ Yes |
| Use it in a client project you're paid for | ✅ Yes |
| Fork it, study it, publish patches | ✅ Yes |
| Launch "AcmePDF.com", a PDF API service built on this code | ❌ No — talk to us |
| Resell access to a PdfWerk instance you host for other companies | ❌ No — talk to us |

The line is roughly: *are you using PdfWerk, or are you selling PdfWerk?* Using it — even
commercially, even as part of something you charge for — is fine. Reselling it as the product
itself is what's reserved.

If you're unsure which side of the line you're on, ask. A commercial licence for the reserved
cases is available.

## Why not MIT?

The project started under MIT and moved before anyone had cloned it. The reasoning:

- MIT is **irreversible**. Once code ships under it, that grant is perpetual — you can loosen a
  licence later, never tighten it. Choosing MIT is a decision you can only make once.
- BSL costs almost every user nothing. The overwhelming majority of use — internal, commercial,
  embedded, modified — is unaffected.
- It protects the only thing worth protecting: someone taking the code and running it as a rival
  hosted service, which MIT expressly permits.
- It has an expiry date. This is not a permanent enclosure; the code becomes fully open in 2030.

## Is this "open source"?

Strictly, no. BSL is **source-available**: the source is public, forkable and auditable, but the
Open Source Initiative does not certify licences with use restrictions. It becomes genuinely
open source (Apache-2.0) on the Change Date.

We'd rather be accurate about that than stretch the term.

## What your dependencies mean for you

Every library PdfWerk depends on is MIT, Apache-2.0, BSD or the PostgreSQL licence — all
permissive, none copyleft, none revenue-gated. So self-hosting introduces no licensing obligations
beyond this one. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

LibreOffice (MPL-2.0) is bundled in the Docker image for Word conversion. It runs as a separate
process and is not linked into PdfWerk. See [NOTICE](NOTICE).

## Commercial licensing

If you need terms the BSL doesn't grant — hosting it as a service, or simply wanting a
conventional commercial agreement with warranties and support — that's available. Open an issue
or contact the licensor.

## Trademark

The **PdfWerk** name and logo are not licensed by the BSL. You may fork the code; please don't
call your fork PdfWerk, and don't imply endorsement.
