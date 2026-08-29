# Deploying PdfWerk

Notes for putting this on **pdfwerk.com**. Nothing here is required to run it locally — see the
README's quick start for that.

## Before anything else

Two settings are the difference between a service that enforces its limits and one that only
appears to.

### 1. Redis, or your quotas are fiction

```
ConnectionStrings__Redis=your-redis:6379
```

Without it the limiter counts **in process**. One instance is fine. Two behind a load balancer
each enforce the quota separately, so the effective limit doubles — and nothing visibly breaks,
which is what makes it dangerous. The app logs a warning at startup when it detects this outside
development.

### 2. Forwarded headers, but only behind your own proxy

```
Client__TrustForwardedHeaders=true      # ONLY when a proxy you control sets X-Forwarded-For
Client__AddressSalt=<openssl rand -hex 32>
```

Anonymous callers are rate limited by their address. Behind a reverse proxy every request appears
to come from the proxy, so **all anonymous users would share one bucket** — the first few
requests each minute would exhaust it for everybody.

But enabling it when the app is directly reachable is worse: anyone can set `X-Forwarded-For` to a
random value on every request and mint unlimited identities, which removes anonymous rate limiting
entirely. Enable it if and only if traffic cannot reach the app except through your proxy.

The salt is not optional either. Addresses are stored hashed, but IPv4 has only ~4 billion values,
so an unsalted hash is reversible by brute force in seconds.

## DNS

GoDaddy holds the nameservers (`ns17/ns18.domaincontrol.com`). In **My Products → DNS**:

| Type | Name | Value | Notes |
| --- | --- | --- | --- |
| `A` | `@` | your server's IPv4 | |
| `AAAA` | `@` | your server's IPv6 | if you have one |
| `CNAME` | `www` | `pdfwerk.com` | |

If you put Cloudflare in front, change the nameservers at GoDaddy to Cloudflare's instead and set
the records there. Cloudflare's proxy also terminates TLS, which removes the Caddy step below —
but set `Client__TrustForwardedHeaders=true` and use `CF-Connecting-IP`, because Cloudflare
rewrites the source address.

## TLS and reverse proxy

Caddy is the least effort — it obtains and renews certificates automatically:

```caddyfile
pdfwerk.com, www.pdfwerk.com {
    encode zstd gzip

    # Uploads are capped per tier inside the app; this is the outer backstop.
    request_body {
        max_size 128MB
    }

    reverse_proxy localhost:8080 {
        header_up X-Forwarded-For {remote_host}
    }
}
```

Then set `Client__TrustForwardedHeaders=true`, since Caddy is now the only path in.

## Running it

```bash
git clone https://github.com/joshihrn/PdfWerk.git
cd PdfWerk
cp .env.example .env       # set POSTGRES_PASSWORD and ADDRESS_SALT
docker compose up -d --build
```

Podman works identically — `podman compose up -d --build`.

The compose stack brings up the API, Postgres and Redis, and the image installs LibreOffice plus
the fonts PDFsharp needs on Linux.

### Environment worth setting in production

```bash
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Redis=redis:6379
ConnectionStrings__Postgres=Host=postgres;Database=pdfwerk;Username=pdfwerk;Password=…
Client__TrustForwardedHeaders=true
Client__AddressSalt=…
Ai__Gemini__ApiKey=…                    # optional; only summarise needs it
RateLimits__FailClosed=true             # reject rather than wave traffic through if Redis dies
```

## Before you open it to the public

- [ ] Redis configured, and confirmed by the absence of the startup warning
- [ ] `ADDRESS_SALT` set to something random, not the example value
- [ ] `TrustForwardedHeaders` matches your actual topology
- [ ] Upload limit at the proxy matches or exceeds the app's tier limits
- [ ] `GET /health` wired to your uptime monitor
- [ ] Anonymous tier limits reviewed for your traffic — the defaults are deliberately tight
- [ ] Postgres backups running: revoking a leaked key needs the key table

## Schema management

The app calls `EnsureCreated()` at startup, which creates the schema if absent but **will not
alter an existing one**. That is fine for a first deployment. Before the first release that
changes a table, switch to EF migrations:

```bash
dotnet ef migrations add InitialCreate --project src/PdfWerk.Infrastructure --startup-project src/PdfWerk.Api
```

and replace the `EnsureCreatedAsync` call in `InfrastructureServiceCollectionExtensions` with
`MigrateAsync`. Skipping this means a schema change silently does not apply.

## Scaling

The API is stateless — all shared state lives in Redis and Postgres — so it scales horizontally
once Redis is configured. Two things to watch:

**LibreOffice is the expensive path.** Each conversion is a process with its own profile
directory. Under load it dominates CPU and memory. If Word conversion is heavy for you, run those
instances separately and route `/v1/create/word` to them.

**Summarise is bounded by the provider, not by you.** Free tiers have their own rate limits, and
hitting them surfaces as a 503 from the provider. The per-action quota on `Summarize` is set low
partly for that reason.

## Costs

| | |
| --- | --- |
| Domain | ~$11–20/yr at GoDaddy on renewal |
| Small VPS (2 vCPU, 4 GB) | ~$12–24/mo — enough for the whole stack including LibreOffice |
| Gemini free tier | $0 |
| TLS via Caddy or Cloudflare | $0 |

## The live deployment

Running on Azure App Service for Containers in `westus2`, in resource group `rg_pdfwerk`.

| Resource | Name | Note |
| --- | --- | --- |
| Container registry | `pdfwerkacr` | Basic. Pulled by managed identity, so no registry password is stored anywhere. |
| App Service plan | `asp-pdfwerk` | Linux B1, pinned to **one instance**. |
| Web app | `pdfwerk-api` | https://pdfwerk-api.azurewebsites.net |
| Database | `pdfwerk` on `vm-sql-quintara` | Shares the Postgres server Quitara already pays for. |

One instance is deliberate, not incidental. The rate limiter counts in-process without Redis, so a
second instance would silently double every published limit. Scaling out requires setting
`ConnectionStrings:Redis` first.

Rebuild and release:

```bash
az acr build --registry pdfwerkacr --image pdfwerk:latest --file Dockerfile .
az webapp restart -g rg_pdfwerk -n pdfwerk-api
```

### DNS for pdfwerk.com

| Type | Name | Value | Purpose |
| --- | --- | --- | --- |
| A | `@` | `13.77.182.13` | Points the apex at the app |
| TXT | `asuid` | `88326D3F114984ED494F915D1B63F14EB586A4B58A3446A498FF4123F42202F8` | Proves domain ownership to App Service |
| TXT | `@` | `MS=ms34650661` | Microsoft 365 ownership |
| TXT | `@` | `brevo-code:f24c4a81e37ade215b9b0f119ae73bd0` | Brevo ownership |
| CNAME | `brevo1._domainkey` | `b1.pdfwerk-com.dkim.brevo.com` | DKIM |
| CNAME | `brevo2._domainkey` | `b2.pdfwerk-com.dkim.brevo.com` | DKIM |
| TXT | `_dmarc` | `v=DMARC1; p=none; rua=mailto:rua@dmarc.brevo.com` | DMARC |

The existing MX records pointing at `smtp.secureserver.net` must be **replaced** with Microsoft's,
or mail to `support@pdfwerk.com` goes to GoDaddy's parking rather than the mailbox.

An A record rather than a CNAME because GoDaddy cannot put a CNAME at the apex. The address is
stable for the life of the app, but it is not reserved — deleting and recreating the app gets a
new one, and the A record would need updating.

### Settings that are not in source control

Four values are set on the app rather than committed, because three are secrets and the fourth is
a bootstrap credential:

```bash
az webapp config appsettings set -g rg_pdfwerk -n pdfwerk-api --settings   ConnectionStrings__Postgres="Host=vm-sql-quintara.postgres.database.azure.com;Database=pdfwerk;Username=<user>;Password=<password>;SSL Mode=Require"   Ai__Gemini__ApiKey="<gemini key>"   Contact__ApiKey="<brevo key>"   Admin__BootstrapKey="pw_<at least 24 characters>"
```

Remove `Admin:BootstrapKey` once you have signed in to `/admin` — a bootstrap credential left in
configuration is a standing back door.

Until `ConnectionStrings:Postgres` is set the app falls back to SQLite **inside the container**,
which is wiped on every restart and every redeploy. API keys, the request log and the block list
all vanish with it. Nothing appears broken, which is what makes it worth stating plainly.

