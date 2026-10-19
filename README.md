# ArbiScanner.McpServer
[![Quality gate](https://sonarcloud.io/api/project_badges/quality_gate?project=dimasdom_ArbiSpreadScanner.McpServer)](https://sonarcloud.io/summary/new_code?id=dimasdom_ArbiSpreadScanner.McpServer)

A .NET 10 [Model Context Protocol](https://modelcontextprotocol.io) server that exposes two
read-only tools — the currently-open arbitrage spread opportunities for the caller's own
ArbiScanner account, and the detail of a single spread by id — by calling `ArbiScannerWeb.API`
over plain HTTP. It is a thin proxy: it owns no data of its own, mints no tokens of its own,
and contains no business logic beyond JWT validation and response mapping.

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
comes back, and this server never mints or exchanges a token of its own.

---

## How authentication works

There is exactly **one** token in play end to end, and this server just validates and
forwards it — it is a plain OAuth resource server, nothing more:

```
ArbiScannerWeb.Client ("MCP Access" page)
      |  user is already logged in (arbiscanner-web-spa session)
      v
ArbiScannerWeb.API's McpTokenController -> McpTokenService
      |  Standard Token Exchange (RFC 8693): exchanges the user's own
      |  session token for a new access token scoped to arbiscanner-mcp
      |  (aud includes both arbiscanner-mcp and arbiscanner-web-spa)
      v
Keycloak (arbiscanner-web realm, arbiscanner-mcp client — whose
          access.token.lifespan is overridden to 30 days, see the
          realm export; scope=offline_access is requested purely to
          make the underlying session an *offline* session so that
          override isn't capped by the realm's ~10h SSO Session Max —
          Standard Token Exchange does not actually issue a usable
          refresh_token here, confirmed against a real container)
      v
one long-lived (30-day) access token ── user copies this into their MCP client config

────────────────────────────────────────────────────────────────────────

MCP client (Claude Desktop, etc.)
      |  Authorization: Bearer <that same access token>
      v
ArbiScanner.McpServer — standard AddJwtBearer resource-server validation
      |  (Authority=arbiscanner-web realm, Audience=arbiscanner-mcp;
      |   local JWKS-based validation, no Keycloak round-trip per request)
      v
SpreadTools.GetSpreadsForCurrentUser / GetSpreadById -> ISpreadsApiClient
      |  forwards the SAME validated token as-is (it already carries the
      |  arbiscanner-web-spa audience too)
      v
GET /api/TradeOpportunity/GetSpreadsForUser | GetSpreadInfo/{id}
      v
ArbiScannerWeb.API   (resolves "current user" from that token's own sub claim —
                       so this is genuinely the token owner's data, nothing new
                       to build there)
```

The one non-obvious piece: getting a *long-lived* credential out of Keycloak. MCP clients
like Claude Desktop store one static bearer value and never do an OAuth refresh dance
themselves, and a normal Keycloak access token is short-lived (minutes). Two things not
usually needed together make the long-lived token work: the `arbiscanner-mcp` client's own
`access.token.lifespan` attribute is overridden to 30 days (see
`keycloak/realm-export/arbiscanner-web-realm.json`), **and** the token exchange request
includes `scope=offline_access` — not to get a refresh token back (it doesn't; confirmed
against a real Keycloak 26.4 container, `refresh_expires_in` comes back `0` either way), but
because requesting that scope is what upgrades the exchange's underlying session to an
*offline* session, which isn't bound by the realm's `SSO Session Max` (~10h default) the way
a normal session is. Drop either half and the resulting token quietly reverts to a
session-bound lifetime instead of the intended 30 days — this was caught by, and is asserted
directly in, `ArbiScannerWeb.IntegrationTests`' `McpTokenControllerTests` (decodes the real
JWT's own `exp` claim rather than trusting the token endpoint's `expires_in` field).

Because the SAME token is forwarded downstream unchanged, there's no separate outbound
credential, no per-request Keycloak call, and no server-held secret in this project at all —
just standard bearer-token validation.

---

## Getting a token

Log into ArbiScannerWeb, go to **MCP Access** in the nav, click **Generate token**, copy it,
and follow the on-page instructions to add it to your MCP client's remote-server config
(URL `https://<domain>/mcp`, `Authorization: Bearer <token>`). Generating a new token does
**not** invalidate previously-generated ones — there is no revocation list yet, so treat the
token like a password and keep track of where you've pasted it.

For manual testing without the UI (e.g. hitting a local dev instance), obtain a token the same
way the load tests do — Resource Owner Password Credentials against the `arbiscanner-mcp`
client:

```bash
curl -s https://auth.arbiscannerwebapp.site/realms/arbiscanner-web/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=arbiscanner-mcp \
  -d client_secret="$KEYCLOAK_MCP_CLIENT_SECRET" \
  -d username=<account-email> \
  -d password=<account-password> \
  | jq -r .access_token
```

Against local Keycloak (`docker-compose.local.yml`), use
`http://localhost:8082/realms/arbiscanner-web/protocol/openid-connect/token` instead. Then call
the server directly, e.g. via an MCP inspector CLI or the real MCP client SDK pointed at
`http://localhost:8087/mcp` with that token as the bearer (Streamable HTTP transport — see the
[MCP transport spec](https://modelcontextprotocol.io)).

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

One interface: `ISpreadsApiClient` (the two outbound calls, returning `FluentResults`'
`Result`/`Result<T>` per this monorepo's convention).

### ArbiScanner.McpServer.Infrastructure

- `SpreadsApiClient` — typed `HttpClient` against `ArbiScannerWeb.API`, reads the current
  request's own bearer token (via `Authentication/BearerTokenReader`) and forwards it as-is,
  deserializes the `SerializableResult<T>` envelope shape. A 401 from `ArbiScannerWeb.API` is
  surfaced as a plain failure, not retried — there's nothing to refresh, since this server
  doesn't hold or manage the token's lifecycle at all.
- `Authentication/BearerTokenReader` — the one place that reads `Authorization: Bearer …` off
  a request, so `SpreadsApiClient` and any future caller agree on exactly what "the presented
  token" means.
- `StartupSetup` — `AddAuthenticationJwt` (a verbatim structural copy of
  `ArbiScannerWeb.Infrastructure.StartupSetup.AddAuthenticationJwt` — deliberately not a new
  invention), plus DI wiring for the `WebApi` HTTP client and `SpreadsApiClient`.

### ArbiScanner.McpServer.Host

Entry point and composition root. `Program.cs` wires DI, standard `AddAuthentication()
.AddJwtBearer(...)`/`AddAuthorization()`, health checks, Serilog, and OpenTelemetry, then calls
`AddMcpServer().WithHttpTransport().WithToolsFromAssembly()`. `MapMcp("/mcp").RequireAuthorization()`
is explicit about both the path (not the SDK's root-mapped default, so it matches whether
reached directly — `docker-compose.local.yml`, port 8087 — or through nginx's `/mcp` route,
which proxies the path through unchanged, see `nginx/nginx.conf`) and the auth requirement;
`/health` is mapped separately with no such requirement, for Docker healthchecks.
`Tools/SpreadTools.cs` is the `[McpServerToolType]` class implementing the two tools above,
constructor-injected with `ISpreadsApiClient` — mirrors how `TradeOpportunityController` sits
directly in `ArbiScannerWeb.API` and calls `ITradeOpportunityService`.

---

## Technologies

| Technology | Role |
|---|---|
| .NET 10 | Runtime and ASP.NET Core host |
| ModelContextProtocol / ModelContextProtocol.AspNetCore | MCP server SDK, Streamable HTTP transport |
| Microsoft.AspNetCore.Authentication.JwtBearer | Resource-server bearer-token validation against Keycloak |
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
  **Standard Token Exchange enabled** and its `access.token.lifespan` override in place (see
  the monorepo root's `keycloak/README.md` step 9 — included in a fresh realm import, but
  needs a one-time manual step on an already-deployed realm, and needs the Keycloak image
  built with `--features=token-exchange`)
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
     up -d postgres kafka kafka-init redis mongodb keycloak web
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
| `Keycloak__Authority` | Keycloak realm issuer (`arbiscanner-web` realm) — resolves JWKS for token validation |
| `Keycloak__Audience` | Expected audience claim on incoming tokens — `arbiscanner-mcp` |
| `WebApi__BaseUrl` | Base URL of `ArbiScannerWeb.API`, e.g. `http://web:8080` |
| `Serilog__WriteTo__1__Args__uri` | Grafana Loki endpoint URL |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP gRPC endpoint for Grafana Tempo |
| `Observability__Enabled` | Toggles OpenTelemetry wiring (`true`/`false`) |

Note this service holds **no client secret** — it only validates tokens, it never calls
Keycloak itself. The root monorepo's `sample.env` documents `KEYCLOAK_MCP_CLIENT_ID` /
`KEYCLOAK_MCP_CLIENT_SECRET` / `MCP_WEB_API_URL`, which `docker-compose.yml` maps onto the
variables above (client id/secret are only actually used by `ArbiScannerWeb.API`'s
`McpTokenService`, to mint tokens — this server doesn't need them, but the id needs to match
what tokens are actually audienced to).

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
build`/`test` against `ArbiScanner.McpServer.slnx` (unit **and** integration tests — the
integration suite needs no Docker/Testcontainers, see [Testing](#testing), so there's nothing
extra for CI to provision), and published `.trx` test results. No sibling repos need checking
out (see [Docker Build](#docker-build)).

### `deploy.yml`

A `workflow_dispatch`-triggered workflow that calls the monorepo root's reusable
`deploy-service.yml`, pinned to a specific commit SHA, with this repo's specifics: solution
file, unit test project, empty `sibling_repos`, and a single image spec
(`arbiscanner-mcp-server`, build context `ArbiScanner.McpServer`). End to end: unit tests +
quality gate → build and push `ghcr.io/dimasdom/arbiscanner-mcp-server` to GHCR → (unless
`dry_run: true`) SSH into the VPS and restart `mcp-server`.

---

## Testing

Two test projects — both run without Docker or any external dependency:

### `ArbiScanner.McpServer.Tests` (unit)

xUnit + FluentAssertions + Moq, covering `SpreadsApiClient` against a stub
`HttpMessageHandler`: forwards the caller's own token unchanged, URL construction (including
id escaping), envelope success/failure mapping, a downstream 401 failing cleanly without any
retry, missing-inbound-token handling, and malformed-response handling.

```bash
cd ArbiScanner.McpServer
dotnet test ArbiScanner.McpServer.Tests/ArbiScanner.McpServer.Tests.csproj
```

### `ArbiScanner.McpServer.IntegrationTests`

Hosts the **real** app via `WebApplicationFactory<Program>` — real DI graph, real middleware
pipeline, real `MapMcp`/tool registration, real JWT bearer validation (against a forged but
correctly-signed test token, the same fast pattern `ArbiScannerWeb.IntegrationTests` uses for
most of its suite — see that project's `JwtTestSettings`/`JwtTestTokenFactory`) — with only the
downstream `ArbiScannerWeb.API` call stubbed. Tool calls are driven through the **real MCP
client SDK** (`ModelContextProtocol.Client`'s `HttpClientTransport` + `McpClient`), not
hand-rolled JSON-RPC, so the actual Streamable HTTP protocol round-trip is exercised, not just
raw HTTP status codes.

| Test | Proves |
|---|---|
| `Health_ReturnsOk_WithoutAuthentication` | `/health` needs no credential |
| `Mcp_WithoutBearerToken_ConnectFailsAndNeverReachesWebApi` | Unauthenticated MCP connections are rejected before any tool can run |
| `Mcp_WithExpiredToken_ConnectFailsAndNeverReachesWebApi` | Token lifetime is actually enforced |
| `CallTool_GetSpreadsForCurrentUser_ForwardsCallersOwnTokenAndReturnsData` | The *exact* token the caller presented reaches `ArbiScannerWeb.API` unchanged, and a real MCP tool-call round-trip returns the expected data |
| `CallTool_GetSpreadById_UrlEncodesIdAndSurfacesWebApiFailureAsToolText` | Id escaping, and downstream failures surface as tool text rather than throwing |

Standard Token Exchange against a real Keycloak — i.e. proving `KEYCLOAK_MCP_CLIENT_ID`/`_SECRET`
and the `access.token.lifespan` override actually produce a usable 30-day token — is covered
separately, with a real Testcontainers Keycloak, by `ArbiScannerWeb.IntegrationTests`'
`McpTokenControllerTests`; re-proving that here would just be slower, not more correct.

```bash
cd ArbiScanner.McpServer
dotnet test ArbiScanner.McpServer.IntegrationTests/ArbiScanner.McpServer.IntegrationTests.csproj
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
│       └── ISpreadsApiClient.cs
│
├── ArbiScanner.McpServer.Infrastructure/
│   ├── Services/
│   │   └── SpreadsApiClient.cs
│   ├── Authentication/
│   │   └── BearerTokenReader.cs
│   └── StartupSetup.cs
│
├── ArbiScanner.McpServer.Host/
│   ├── Tools/
│   │   └── SpreadTools.cs              # [McpServerToolType] — the two MCP tools
│   ├── Program.cs
│   ├── appsettings.template.json
│   └── appsettings.Development.template.json
│
├── ArbiScanner.McpServer.Tests/                  # unit
├── ArbiScanner.McpServer.IntegrationTests/        # WebApplicationFactory + real MCP client SDK
├── Dockerfile
└── ArbiScanner.McpServer.slnx
```
