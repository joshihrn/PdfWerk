# Security policy

## Reporting a vulnerability

**Please do not open a public issue.** PdfWerk parses untrusted files from the public internet, so
a parser or rate-limiting flaw is exploitable the moment it is described publicly.

Use GitHub's [private vulnerability reporting](https://github.com/joshihrn/PdfWerk/security/advisories/new),
which is enabled on this repository.

Please include what you would want to receive: what you did, what happened, what you expected, and
a proof of concept if you have one. A minimal PDF or request that triggers the issue is worth more
than a paragraph describing it.

You'll get an acknowledgement within a few days. This is a small project, not a vendor with an
on-call security team — expect honesty about timelines rather than an SLA.

## What is in scope

Things worth reporting:

- **Parser flaws** — a crafted PDF or .docx causing memory exhaustion, an unbounded loop, a crash,
  or code execution.
- **Rate-limit bypass** — any way to exceed the documented quotas, including forging identity to
  obtain a fresh anonymous bucket.
- **API key weaknesses** — recovering a key from stored data, using a revoked or expired key, or
  reading another caller's key or usage.
- **Prompt injection with real consequences** — document content that causes the summarizer to do
  something beyond producing a wrong summary. The document is fenced and declared untrusted, but
  fencing is mitigation, not proof.
- **SSRF** via a configured provider `BaseUrl`, or any request the server can be induced to make.
- **Information disclosure** — errors, headers or logs revealing another user's document content,
  API keys, or internal paths.

## What is not in scope

- **Permission flags on a protected PDF being ignored.** These are advisory by design in the PDF
  specification, honoured voluntarily by readers. `/v1/protect` says so in its own response. Only
  the user password provides actual encryption.
- **Rate limits being per-process when Redis is not configured.** This is documented, warned about
  at startup, and is a deployment choice rather than a defect.
- **Denial of service by simply sending lots of traffic.** That's what the rate limiter is for;
  report it if you can get *past* the limiter.
- Findings from automated scanners with no demonstrated impact.
- Missing hardening headers on the demo site, absent a concrete exploit.

## Known limitations, stated honestly

- **Uploaded documents are processed in memory** and not persisted, but they do pass through the
  server. Do not send material you cannot share with the operator of the instance.
- **Summarisation sends document text to a third-party model provider** unless you configure
  Ollama for local inference. This is inherent to the feature, and the provider is reported in
  every response.
- **The anonymous rate limit is keyed on a salted hash of the caller's IP.** Anyone able to change
  their address freely can obtain new buckets. An API key is the stronger identity.

## Disclosure

Report privately, and please allow a fix to ship before publishing. Credit is given in the release
notes unless you'd rather not be named.
