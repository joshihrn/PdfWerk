# Contributing to PdfWerk

Contributions are welcome. Please read the CLA section first — it is short, and it is a hard
requirement rather than a formality.

## The CLA, and why it exists

**Every contribution requires agreement to the [Contributor Licence Agreement](CLA.md).**

PdfWerk is [BSL 1.1](LICENSE) and commercial licences are sold for uses the BSL reserves. That
only works if one party holds the rights to the whole codebase. Without a CLA, each contributor
retains copyright in their own commits — which would mean the project could not be relicensed,
could not convert to Apache-2.0 on the Change Date, and could not be licensed commercially,
without tracking down every contributor who ever landed a patch and getting each to agree.

Projects that skip this discover the problem years later, when the contributor list is long and
some of it is unreachable. So it's enforced from the first contribution.

The CLA does **not** take your rights away: you keep full ownership of your contribution and can
use it however you like elsewhere. It grants the project a licence broad enough to relicense.

**How to sign:** tick the CLA box in the pull request template. That's it — no forms, no email.

## Before you start

For anything beyond a small fix, **open an issue first**. It's disheartening to write a feature
and then learn it doesn't fit, and that's on the maintainer to prevent, not you.

Good candidates: bug fixes with a failing test, new PDF operations, provider integrations,
accessibility improvements, documentation.

Likely to be declined: dependencies with copyleft or revenue-gated licences (see below), large
refactors without prior discussion, and features that increase the attack surface of a public
endpoint without a matching rate-limit story.

## Setting up

```bash
git clone https://github.com/joshihrn/PdfWerk.git
cd PdfWerk
npm --prefix web ci && npm --prefix web run build:all
dotnet run --project src/PdfWerk.Api
```

Runs on <http://localhost:5272> with no infrastructure — SQLite and in-process rate limiting.
For the full stack including Redis, Postgres and LibreOffice:

```bash
cp .env.example .env    # set POSTGRES_PASSWORD and ADDRESS_SALT
docker compose up --build
```

Podman works too: `podman compose up --build`.

## Standards

**Tests.** `dotnet test` must pass. A bug fix should come with a test that fails without it —
several of the nastier bugs in this codebase were found by tests written *before* the fix, and
that's the pattern worth keeping.

**Licences.** Any new dependency must be MIT, Apache-2.0, BSD or similarly permissive. No GPL,
LGPL, AGPL, or source-available licences, and nothing that changes terms above a revenue
threshold. If you add one, update [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) with the
licence read from the package metadata, not from its README.

**Untrusted input.** Every endpoint parses files from the public internet. A parse failure must
surface as a 4xx, never an unhandled exception. If you touch parsing, add a case to
`HardeningTests`.

**Rate limiting.** New endpoints go through `ActionRunner` so quota enforcement is automatic. An
endpoint that bypasses it is the one that gets abused.

**Comments.** Explain *why*, not *what*. The code already says what it does; what it can't say is
which alternative you rejected and why. Comments that restate the line above will be removed.

## Commit messages

Explain the reasoning, not just the change. "Fix null check" says nothing; "Reject truncated
model replies, since a half-summary parses as valid but is wrong" says why the change exists.

## Security

Do **not** open a public issue for a security problem. See [SECURITY.md](SECURITY.md).

## Code of conduct

Be decent. Assume good faith, critique code rather than people, and accept that maintainers may
decline a change without it being a judgement on you. Harassment of any kind means you're done
here.
