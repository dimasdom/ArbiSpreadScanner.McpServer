# ArbiScanner.McpServer
[![Quality gate](https://sonarcloud.io/api/project_badges/quality_gate?project=dimasdom_ArbiSpreadScanner.McpServer)](https://sonarcloud.io/summary/new_code?id=dimasdom_ArbiSpreadScanner.McpServer)

A .NET 10 [Model Context Protocol](https://modelcontextprotocol.io) server that exposes two
read-only tools — the currently-open arbitrage spread opportunities for the caller's own
ArbiScanner account, and the detail of a single spread by id — by calling `ArbiScannerWeb.API`
over plain HTTP. It is a thin proxy: it owns no data of its own and contains no business logic
beyond authentication and response mapping.

---

## Table of Contents

- [Overview](#overview)
- [How authentication works](#how-authentication-works)
- [Getting a token](#getting-a-token)
- [MCP Tools](#mcp-tools)
- [Project Layer Responsibilities](#project-layer-responsibilities)
- [Technologies](#technologies)
- [Prerequisites](#prerequisites)
- [Running Locally](#running-locally)
- [Environment Variables](#environment-variables)
- [Docker Build](#docker-build)
- [CI/CD](#cicd)
- [Testing](#testing)
- [Project Structure](#project-structure)

---

## Overview

This server exists so that MCP clients — an in-app chat assistant (future work, see
`docs/investigations/ai-chat-assistant.md` in the monorepo root) or a general-purpose client
like Claude Desktop — can query ArbiScanner spread data through the standard MCP tool-calling
interface instead of a bespoke API integration.

It is **multi-tenant by pass-through**: any user can generate their own personal token from
ArbiScannerWeb.Client's "MCP Access" page and use it in their own MCP client, and every tool
call is scoped to *that* user's account, not a shared/fixed one. There is no per-server
"which account" configuration — the caller's presented token is what determines whose data
comes back.

---

## How authentication works

There is exactly **one** token in play, and it plays two roles at once — worth being explicit
about, since it's not how a typical resource server works:

```
ArbiScannerWeb.Client ("MCP Access" page)
      |  user is already logged in (arbiscanner-web-spa session)
      v
ArbiScannerWeb.API's McpTokenController -> McpTokenService
      |  Standard Token Exchange (RFC 8693): exchanges the user's own
      |  session token for an offline_access refresh token scoped to
      |  the arbiscanner-mcp client
      v
Keycloak (arbiscanner-web realm, arbiscanner-mcp client)
      |
      v
long-lived offline_access refresh token  ── user copies this into their MCP client config

────────────────────────────────────────────────────────────────────────

MCP client (Claude Desktop, etc.)
      |  Authorization: Bearer <that same refresh token>
      v
ArbiScanner.McpServer's McpAuthenticationMiddleware
      |  grant_type=refresh_token against the SAME arbiscanner-mcp client
      v
Keycloak                                        (cached per presented token,
      |                                          ~30s-before-expiry refresh —
      v                                          see KeycloakRefreshTokenProvider)
short-lived access token, aud=[arbiscanner-mcp, arbiscanner-web-spa]
      |
      |  SpreadTools.GetSpreadsForCurrentUser / GetSpreadById -> ISpreadsApiClient
      |  forwards the SAME exchanged access token as-is
      v
GET /api/TradeOpportunity/GetSpreadsForUser | GetSpreadInfo/{id}
      v
ArbiScannerWeb.API   (resolves "current user" from that token's own sub claim —
                       so this is genuinely the token owner's data, nothing new
                       to build there)
```

Why a refresh token, not a plain access token: MCP clients like Claude Desktop store one
static bearer value and never do an OAuth refresh dance themselves. A raw Keycloak access
token is short-lived (minutes) and would go stale almost immediately. A refresh token
obtained with `offline_access` is Keycloak's purpose-built mechanism for exactly this —
"long-lived credential, held by something that isn't a browser session." This server does
the actual refreshing on the caller's behalf, once per request (cached), so the token the
user pastes into their MCP client just works indefinitely without them touching it again.

There is no separate "is this caller allowed to hit the server at all" check distinct from
the token exchange itself — a successful `grant_type=refresh_token` call **is** the
authentication check (an invalid, expired, or revoked token simply fails the exchange and the
middleware returns 401). See `KeycloakOptions` for the `arbiscanner-mcp` client config both
`ArbiScannerWeb.API` (to mint tokens) and this server (to redeem them) share.

---

## Getting a token

Log into ArbiScannerWeb, go to **MCP Access** in the nav, click **Generate token**, copy it,
and follow the on-page instructions to add it to your MCP client's remote-server config
(URL `https://<domain>/mcp`, `Authorization: Bearer <token>`). Generating a new token does
**not** invalidate previously-generated ones — there is no revocation list yet, so treat the
token like a password and keep track of where you've pasted it.

For manual testing without the UI (e.g. hitting a local dev instance), obtain a token the same
way the load tests do — Resource Owner Password Credentials against the `arbiscanner-mcp`
client, requesting `offline_access`:

```bash
curl -s https://auth.arbiscannerwebapp.site/realms/arbiscanner-web/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=arbiscanner-mcp \
  -d client_secret="$KEYCLOAK_MCP_CLIENT_SECRET" \
  -d scope=offline_access \
  -d username=<account-email> \
  -d password=<account-password> \
  | jq -r .refresh_token
```

Against local Keycloak (`docker-compose.local.yml`), use
`http://localhost:8082/realms/arbiscanner-web/protocol/openid-connect/token` instead. Then call
the server directly, e.g. via an MCP inspector CLI or
`curl -X POST http://localhost:8087/mcp -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{...}'`
(Streamable HTTP JSON-RPC — see the [MCP transport spec](https://modelcontextprotocol.io) for
the request shape).

---

## MCP Tools

| Tool | Wraps | Notes |
|---|---|---|
| `get_spreads_for_current_user` | `GET /api/TradeOpportunity/GetSpreadsForUser` | All currently-open spreads matching the caller's own settings filters. |
| `get_spread_by_id` | `GET /api/TradeOpportunity/GetSpreadInfo/{id}` | Detail + ticker history for one spread. `id` is the spread's GUID. |

Both require the caller's account to have an active subscription — `ArbiScannerWeb.API`
returns a `Forbidden` result otherwise, which surfaces as a tool error string, not an exception.

---

## Project Layer Responsibilities

Clean Architecture, four projects, no `Application` layer — both tools are thin pass-throughs
(call an HTTP client, map the result), the same reasoning the monorepo root's `CLAUDE.md` gives
for `ArbitrageScanner` skipping that layer too.

### ArbiScanner.McpServer.Domain

DTOs mirroring the wire shape of `ArbiScannerWeb.API`'s `TradeOpportunityDetailsDto` (own
copies, not a project reference — this service only ever talks to that API over HTTP, never
references its assemblies) plus two configuration POCOs (`KeycloakOptions`, `WebApiOptions`).
No external package dependencies.

### ArbiScanner.McpServer.Abstractions

Two interfaces: `ISpreadsApiClient` (the two outbound calls, returning `FluentResults`'
`Result`/`Result<T>` per this monorepo's convention) and `IOutboundTokenProvider` (the
caller-presented-refresh-token-to-access-token exchange).

### ArbiScanner.McpServer.Infrastructure

- `SpreadsApiClient` — typed `HttpClient` against `ArbiScannerWeb.API`, attaches whatever
  access token `IOutboundTokenProvider` returns for the current request's presented token,
  deserializes the `SerializableResult<T>` envelope shape, retries once on 401 after a forced
  token refresh.
- `KeycloakRefreshTokenProvider` — the refresh-token grant exchange, caching per *presented*
  token (never the raw token itself — see its `ComputeCacheKey`, which hashes it) with an
  `IMemoryCache` and a per-token `SemaphoreSlim`-guarded double-checked refresh, so concurrent
  tool calls from the *same* caller don't each trigger their own Keycloak round-trip, without
  serializing unrelated callers against each other.
- `Authentication/BearerTokenReader` — the one place that reads `Authorization: Bearer …` off
  a request, shared by the middleware below and `SpreadsApiClient` so both agree on exactly
  what "the presented token" means.
- `Authentication/McpAuthenticationMiddleware` — gates `/mcp`: extracts the presented token,
  exchanges it via `IOutboundTokenProvider`, and 401s if that fails. This *is* the
  authentication check — there's no separate JWT-validation step, because the presented
  string isn't a genuine access token, it's the refresh token from [Getting a
  token](#getting-a-token).
- `StartupSetup` — DI wiring for the HTTP clients, memory cache, and services above.

### ArbiScanner.McpServer.Host

Entry point and composition root. `Program.cs` wires DI, health checks, Serilog, and
OpenTelemetry, calls `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()`, and applies
`McpAuthenticationMiddleware` only to the `/mcp` path (`app.UseWhen(...)`) so `/health` stays
open for Docker healthchecks with no credential. `MapMcp("/mcp")` is explicit (not the SDK's
root-mapped default) so it matches whether reached directly (`docker-compose.local.yml`, port
8087) or through nginx's `/mcp` route, which proxies the path through unchanged — see
`nginx/nginx.conf`. `Tools/SpreadTools.cs` is the `[McpServerToolType]` class implementing the
two tools above, constructor-injected with `ISpreadsApiClient` — mirrors how
`TradeOpportunityController` sits directly in `ArbiScannerWeb.API` and calls
`ITradeOpportunityService`.

---

## Technologies

| Technology | Role |
|---|---|
| .NET 10 | Runtime and ASP.NET Core host |
| ModelContextProtocol / ModelContextProtocol.AspNetCore | MCP server SDK, Streamable HTTP transport |
| FluentResults | Result-type error handling, matching the rest of this monorepo |
| Serilog + Serilog.Sinks.GrafanaLoki | Structured logging shipped to Grafana Loki |
| OpenTelemetry SDK + OTLP exporter | Distributed tracing to Grafana Tempo |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | `/metrics` endpoint for Prometheus scraping |
| Docker | Containerised deployment |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A running `ArbiScannerWeb.API` instance
- The `arbiscanner-mcp` client provisioned in the `arbiscanner-web` Keycloak realm, with
  **Standard Token Exchange enabled** (see the monorepo root's `keycloak/README.md` step 9 —
  included in a fresh realm import, but needs a one-time manual step on an already-deployed
  realm, and needs the Keycloak image built with `--features=token-exchange`)
- A personal token — see [Getting a token](#getting-a-token)

---

## Running Locally

1. Clone the monorepo and initialise submodules:

   ```bash
   git clone --recurse-submodules https://github.com/dimasdom/SpreadScanner.git
   ```

2. Start infrastructure + `web` + local Keycloak (from the monorepo root):

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.local.yml \
     up -d postgres rabbitmq redis mongodb keycloak web
   ```

3. Follow the monorepo root's `keycloak/README.md` "Local development" section, then run
   `./keycloak/configure-local-dev.sh` — it also sets a known secret on `arbiscanner-mcp`.

4. Get a token (either through `web`'s "MCP Access" page if you're running the client too, or
   the manual curl in [Getting a token](#getting-a-token)).

5. Run the host:

   ```bash
   cd ArbiScanner.McpServer.Host
   dotnet run
   ```

6. Point an MCP client (or `curl`) at `http://localhost:5xxx/mcp` (the port `dotnet run`
   prints) with your token as the bearer.

---

## Environment Variables

All `appsettings.json` keys are overridable via environment variables using the standard
ASP.NET Core `__` double-underscore convention.

| Variable | Description |
|---|---|
| `Keycloak__Authority` | Keycloak realm issuer (`arbiscanner-web` realm) — resolves the token endpoint |
| `Keycloak__ClientId` | `arbiscanner-mcp` confidential client id |
| `Keycloak__ClientSecret` | `arbiscanner-mcp` client secret |
| `WebApi__BaseUrl` | Base URL of `ArbiScannerWeb.API`, e.g. `http://web:8080` |
| `Serilog__WriteTo__1__Args__uri` | Grafana Loki endpoint URL |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP gRPC endpoint for Grafana Tempo |
| `Observability__Enabled` | Toggles OpenTelemetry wiring (`true`/`false`) |

The root monorepo's `sample.env` documents the corresponding `KEYCLOAK_MCP_CLIENT_ID` /
`KEYCLOAK_MCP_CLIENT_SECRET` / `MCP_WEB_API_URL` variables that `docker-compose.yml` maps onto
the ones above for the containerised deployment — the same `arbiscanner-mcp` client
credentials are also used by `ArbiScannerWeb.API`'s `McpTokenService` to mint tokens.

---

## Docker Build

Unlike `ArbiScanner.TelegramNotifierApp`, this service has **no cross-submodule project
references** — it only calls `ArbiScannerWeb.API` over HTTP — so the build context is this
submodule's own root, not the monorepo root:

```bash
# From ArbiScanner.McpServer/
docker build -t arbiscanner-mcp-server:latest .
```

From the monorepo root, the same image is built as the `mcp-server` service in
`docker-compose.yml` (`context: ArbiScanner.McpServer`). See that file's `mcp-server` block for
the full environment wiring, and `docker-compose.local.yml` for the local-dev override that
points it at local Keycloak and a `dotnet run` `ArbiScannerWeb.API` instead of the containerised
ones, bypassing nginx/TLS for direct local MCP client testing.

---

## CI/CD

This repo has its own GitHub Actions, independent of the monorepo root's Actions tab (it's a
separate git remote — see the monorepo root README's CI/CD section for how the two relate). Two
workflows live under `.github/workflows/`: `ci.yml` and `deploy.yml` — no `load-test.yml`, since
this service has no public REST API to load-test the way `ArbiScannerWeb.API`/`ArbiScannerAdminPannel.API`
do.

### `ci.yml`

Runs on every push/PR to `main`: SonarCloud scan (project
`dimasdom_ArbiSpreadScanner.McpServer`, `sonar.qualitygate.wait=true`), CodeQL, `dotnet
build`/`test` against `ArbiScanner.McpServer.slnx`, and published `.trx` test results. No
sibling repos need checking out (see [Docker Build](#docker-build)).

### `deploy.yml`

A `workflow_dispatch`-triggered workflow that calls the monorepo root's reusable
`deploy-service.yml`, pinned to a specific commit SHA, with this repo's specifics: solution
file, unit test project, empty `sibling_repos`, and a single image spec
(`arbiscanner-mcp-server`, build context `ArbiScanner.McpServer`). End to end: unit tests +
quality gate → build and push `ghcr.io/dimasdom/arbiscanner-mcp-server` to GHCR → (unless
`dry_run: true`) SSH into the VPS and restart `mcp-server`.

---

## Testing

`ArbiScanner.McpServer.Tests` is a unit test project (xUnit + FluentAssertions + Moq) covering
the Infrastructure layer's HTTP-facing and auth logic against a stub `HttpMessageHandler` /
`DefaultHttpContext`:

| File | Coverage |
|---|---|
| `KeycloakRefreshTokenProviderTests` | Cache hit/miss per presented token, independent caching across different callers, the refresh buffer window, `forceRefresh` correctly bypassing a still-valid cache (including inside the double-checked lock), concurrent callers from the *same* caller only triggering one Keycloak round-trip, and failure/exception paths |
| `McpAuthenticationMiddlewareTests` | Missing/non-Bearer header → 401 without calling `next`, failed token exchange → 401, successful exchange → `next` is called |
| `SpreadsApiClientTests` | Bearer token attachment, URL construction (including id escaping), envelope success/failure mapping, the 401-forces-refresh-and-retries-once path, missing-inbound-token handling, and malformed-response handling |

```bash
cd ArbiScanner.McpServer
dotnet test ArbiScanner.McpServer.Tests/ArbiScanner.McpServer.Tests.csproj
```

---

## Project Structure

```
ArbiScanner.McpServer/
├── ArbiScanner.McpServer.Domain/
│   ├── Dtos/
│   │   ├── SpreadDetailsDto.cs, SpreadPositionDto.cs, SpreadTickerDto.cs
│   │   ├── ExchangeRateDto.cs, OrderLevelDto.cs, SpreadEnums.cs
│   │   └── ApiEnvelopeDto.cs           # mirrors ArbiScannerWeb.API's SerializableResult<T>
│   └── Settings/
│       ├── KeycloakOptions.cs
│       └── WebApiOptions.cs
│
├── ArbiScanner.McpServer.Abstractions/
│   └── Interfaces/
│       ├── ISpreadsApiClient.cs
│       └── IOutboundTokenProvider.cs
│
├── ArbiScanner.McpServer.Infrastructure/
│   ├── Services/
│   │   ├── SpreadsApiClient.cs
│   │   └── KeycloakRefreshTokenProvider.cs
│   ├── Authentication/
│   │   ├── BearerTokenReader.cs
│   │   └── McpAuthenticationMiddleware.cs
│   └── StartupSetup.cs
│
├── ArbiScanner.McpServer.Host/
│   ├── Tools/
│   │   └── SpreadTools.cs              # [McpServerToolType] — the two MCP tools
│   ├── Program.cs
│   ├── appsettings.template.json
│   └── appsettings.Development.template.json
│
├── ArbiScanner.McpServer.Tests/
├── Dockerfile
└── ArbiScanner.McpServer.slnx
```
