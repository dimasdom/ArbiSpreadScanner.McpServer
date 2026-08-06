# ArbiScanner.McpServer
[![Quality gate](https://sonarcloud.io/api/project_badges/quality_gate?project=dimasdom_ArbiSpreadScanner.McpServer)](https://sonarcloud.io/summary/new_code?id=dimasdom_ArbiSpreadScanner.McpServer)

A .NET 10 [Model Context Protocol](https://modelcontextprotocol.io) server that exposes two
read-only tools — the currently-open arbitrage spread opportunities for one ArbiScanner
account, and the detail of a single spread by id — by calling `ArbiScannerWeb.API` over plain
HTTP. It is a thin proxy: it owns no data of its own and contains no business logic beyond
authentication and response mapping.

---

## Table of Contents

- [Overview](#overview)
- [The two-token-flow architecture](#the-two-token-flow-architecture)
- [MCP Tools](#mcp-tools)
- [Project Layer Responsibilities](#project-layer-responsibilities)
- [Technologies](#technologies)
- [Prerequisites](#prerequisites)
- [Running Locally](#running-locally)
- [Manual bootstrap (testing only — no UI flow yet)](#manual-bootstrap-testing-only--no-ui-flow-yet)
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

It is deliberately narrow in scope for this phase: **read-only**, **two tools**, **single-tenant**
(every deployed instance always acts on behalf of one specific, pre-configured ArbiScanner
account — there is no per-caller "which account" concept). A future phase may add a real
site-driven OAuth flow so any user can point their own MCP client at their own account; that is
explicitly out of scope here (see the next section).

---

## The two-token-flow architecture

This server has **two separate, unrelated token flows**. Confusing them is the most likely
source of "why is this 401ing" bugs, so they're called out up front:

```
MCP client (Claude Desktop, etc.)
      |  Authorization: Bearer <inbound token, aud=arbiscanner-mcp>
      v
ArbiScanner.McpServer   (validates the inbound token: AddJwtBearer against the
                          arbiscanner-web Keycloak realm — a gate, not a way of
                          identifying "which account", see below)
      |
      |  SpreadTools.GetSpreadsForCurrentUser / GetSpreadById
      v
ISpreadsApiClient  --(needs a Bearer token)-->  IOutboundTokenProvider
      |                                                |
      |                                                | grant_type=refresh_token
      |                                                v
      |                                    Keycloak (arbiscanner-mcp client,
      |                                    ONE fixed account's refresh token,
      |                                    configured via KEYCLOAK_MCP_REFRESH_TOKEN)
      |                                                |
      |<---------------- access token, aud=arbiscanner-web-spa ------------------
      v
GET /api/TradeOpportunity/GetSpreadsForUser | GetSpreadInfo/{id}
      v
ArbiScannerWeb.API
```

- **Inbound** — every MCP request must carry a valid Keycloak access token
  (`aud=arbiscanner-mcp`, `arbiscanner-web` realm) or the server rejects it
  (`app.MapMcp().RequireAuthorization()`). This is purely a "may this caller reach the server at
  all" gate. **The inbound caller's identity is never read by tool logic** — it does not decide
  whose spreads are returned, because there is currently no site-driven flow that would let an
  arbitrary caller's token be *linked* to a specific ArbiScanner account. That link is exactly
  the piece deferred to future work.
- **Outbound** — separately, the server holds one long-lived Keycloak **refresh token**
  (`KEYCLOAK_MCP_REFRESH_TOKEN`) belonging to one specific ArbiScanner account, obtained with
  `scope=offline_access` so it survives the realm's SSO session idle timeout. `KeycloakRefreshTokenProvider`
  exchanges it for short-lived access tokens (`aud=arbiscanner-web-spa` — required, since
  `ArbiScannerWeb.API` validates that specific audience; see its Keycloak client config below),
  caching them in-process and refreshing ~30s before expiry, with a forced-refresh-and-retry
  fallback on a downstream 401. *This* token is what determines whose spreads
  `GetSpreadsForUser`/`GetSpreadInfo` return — always the same one account, for every caller.

Net effect: every properly-authenticated MCP caller sees the *same* account's data. That's
intentional for this phase, not a bug.

---

## MCP Tools

| Tool | Wraps | Notes |
|---|---|---|
| `get_spreads_for_current_user` | `GET /api/TradeOpportunity/GetSpreadsForUser` | All currently-open spreads matching the configured account's settings filters. |
| `get_spread_by_id` | `GET /api/TradeOpportunity/GetSpreadInfo/{id}` | Detail + ticker history for one spread. `id` is the spread's GUID. |

Both require the configured account to have an active subscription — `ArbiScannerWeb.API`
returns a `Forbidden` result otherwise, which surfaces as a tool error string, not an exception.

---

## Project Layer Responsibilities

Clean Architecture, four projects, no `Application` layer — both tools are thin pass-throughs
(call an HTTP client, map the result), the same reasoning the monorepo root's `CLAUDE.md` gives
for `ArbitrageScanner` skipping that layer too.

### ArbiScanner.McpServer.Domain

DTOs mirroring the wire shape of `ArbiScannerWeb.API`'s `TradeOpportunityDetailsDto` (own
copies, not a project reference — this service only ever talks to that API over HTTP, never
references its assemblies) plus the three configuration POCOs (`InboundJwtOptions`,
`OutboundKeycloakOptions`, `WebApiOptions`). No external package dependencies.

### ArbiScanner.McpServer.Abstractions

Two interfaces: `ISpreadsApiClient` (the two outbound calls, returning `FluentResults`'
`Result`/`Result<T>` per this monorepo's convention) and `IOutboundTokenProvider` (the
refresh-token-to-access-token exchange).

### ArbiScanner.McpServer.Infrastructure

- `SpreadsApiClient` — typed `HttpClient` against `ArbiScannerWeb.API`, attaches the outbound
  bearer token, deserializes the `SerializableResult<T>` envelope shape, retries once on 401
  after a forced token refresh.
- `KeycloakRefreshTokenProvider` — the refresh-token grant exchange described above, with an
  in-process cache (no Redis — this service is single-instance, so a shared cache buys nothing)
  and a `SemaphoreSlim`-guarded double-checked refresh so concurrent tool calls don't each
  trigger their own Keycloak round-trip.
- `OutboundTokenHealthCheck` — confirms the refresh token is still exchangeable; the single most
  useful health signal for an otherwise-stateless service.
- `StartupSetup` — `AddInboundAuthenticationJwt` (a verbatim structural copy of
  `ArbiScannerWeb.Infrastructure.StartupSetup.AddAuthenticationJwt` — deliberately not a new
  invention), plus DI wiring for the HTTP clients and services above.

### ArbiScanner.McpServer.Host

Entry point and composition root. `Program.cs` wires DI, inbound JWT auth, health checks,
Serilog, and OpenTelemetry, then calls
`AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` and `MapMcp().RequireAuthorization()`.
`Tools/SpreadTools.cs` is the `[McpServerToolType]` class implementing the two tools above,
constructor-injected with `ISpreadsApiClient` — mirrors how `TradeOpportunityController` sits
directly in `ArbiScannerWeb.API` and calls `ITradeOpportunityService`.

---

## Technologies

| Technology | Role |
|---|---|
| .NET 10 | Runtime and ASP.NET Core host |
| ModelContextProtocol / ModelContextProtocol.AspNetCore | MCP server SDK, Streamable HTTP transport |
| Microsoft.AspNetCore.Authentication.JwtBearer | Inbound resource-server validation against Keycloak |
| FluentResults | Result-type error handling, matching the rest of this monorepo |
| Serilog + Serilog.Sinks.GrafanaLoki | Structured logging shipped to Grafana Loki |
| OpenTelemetry SDK + OTLP exporter | Distributed tracing to Grafana Tempo |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | `/metrics` endpoint for Prometheus scraping |
| Docker | Containerised deployment |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A running `ArbiScannerWeb.API` instance
- The `arbiscanner-mcp` client provisioned in the `arbiscanner-web` Keycloak realm (see the
  monorepo root's `keycloak/README.md` step 9 — it's included in a fresh realm import, but
  needs a one-time manual step on an already-deployed realm)
- A refresh token for one real ArbiScanner account with an active subscription (see
  [Manual bootstrap](#manual-bootstrap-testing-only--no-ui-flow-yet) below)

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
   `./keycloak/configure-local-dev.sh` — it now also sets a known secret on `arbiscanner-mcp`.

4. Complete the [manual bootstrap](#manual-bootstrap-testing-only--no-ui-flow-yet) below to get
   an outbound refresh token, and set it in `appsettings.Development.json` (or environment
   variables — see [Environment Variables](#environment-variables)).

5. Run the host:

   ```bash
   cd ArbiScanner.McpServer.Host
   dotnet run
   ```

6. Point an MCP client (or `curl` — see step 7 of the bootstrap section) at
   `http://localhost:5xxx/mcp` (the port `dotnet run` prints) with an inbound bearer token.

---

## Manual bootstrap (testing only — no UI flow yet)

There is currently no site-driven flow for a user to obtain their own MCP token — that is
future work (see [The two-token-flow architecture](#the-two-token-flow-architecture)). Until
then, both tokens needed to exercise this server end-to-end are obtained manually against
Keycloak's token endpoint, mirroring how `arbiscanner-web-loadtest` already works for this
repo's load tests (`sample.env`'s `WEB_LOADTEST_*` vars).

**1. Outbound refresh token** — for a real ArbiScanner account with an active subscription,
requesting `offline_access` so the token survives the realm's SSO session idle timeout:

```bash
curl -s https://auth.arbiscannerwebapp.site/realms/arbiscanner-web/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=arbiscanner-mcp \
  -d client_secret="$KEYCLOAK_MCP_CLIENT_SECRET" \
  -d scope=offline_access \
  -d username=<real-account-email> \
  -d password=<real-account-password> \
  | jq -r .refresh_token
```

Set the result as `KEYCLOAK_MCP_REFRESH_TOKEN`.

**2. Inbound test token** — any real `arbiscanner-web` account works; it does not need an
active subscription (that check happens server-side against the *outbound* token's account,
not the inbound caller — see the architecture section above):

```bash
curl -s https://auth.arbiscannerwebapp.site/realms/arbiscanner-web/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=arbiscanner-mcp \
  -d client_secret="$KEYCLOAK_MCP_CLIENT_SECRET" \
  -d username=<any-account-email> \
  -d password=<password> \
  | jq -r .access_token
```

Use the result as `Authorization: Bearer <token>` when calling the server directly, e.g. via an
MCP inspector CLI or `curl -X POST http://localhost:8087/mcp/ -H "Authorization: Bearer <token>"
-H "Content-Type: application/json" -d '{...}'` (Streamable HTTP JSON-RPC — see the
[MCP transport spec](https://modelcontextprotocol.io) for the request shape).

Against local Keycloak (`docker-compose.local.yml`), replace the URL above with
`http://localhost:8082/realms/arbiscanner-web/protocol/openid-connect/token`.

---

## Environment Variables

All `appsettings.json` keys are overridable via environment variables using the standard
ASP.NET Core `__` double-underscore convention.

| Variable | Description |
|---|---|
| `Jwt__Authority` | Keycloak realm issuer for **inbound** token validation (`arbiscanner-web` realm) |
| `Jwt__Audience` | Expected audience on inbound tokens — `arbiscanner-mcp` |
| `OutboundKeycloak__Authority` | Keycloak realm issuer used for the outbound refresh-token grant |
| `OutboundKeycloak__ClientId` | `arbiscanner-mcp` confidential client id |
| `OutboundKeycloak__ClientSecret` | `arbiscanner-mcp` client secret |
| `OutboundKeycloak__RefreshToken` | The one ArbiScanner account's long-lived refresh token (`offline_access`) |
| `WebApi__BaseUrl` | Base URL of `ArbiScannerWeb.API`, e.g. `http://web:8080` |
| `Serilog__WriteTo__1__Args__uri` | Grafana Loki endpoint URL |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP gRPC endpoint for Grafana Tempo |
| `Observability__Enabled` | Toggles OpenTelemetry wiring (`true`/`false`) |

The root monorepo's `sample.env` documents the corresponding `OIDC_AUTHORITY_MCP` /
`OIDC_AUDIENCE_MCP` / `KEYCLOAK_MCP_CLIENT_ID` / `KEYCLOAK_MCP_CLIENT_SECRET` /
`KEYCLOAK_MCP_REFRESH_TOKEN` / `MCP_WEB_API_URL` variables that `docker-compose.yml` maps onto
the ones above for the containerised deployment.

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
the Infrastructure layer's HTTP-facing logic against a stub `HttpMessageHandler`:

| File | Coverage |
|---|---|
| `KeycloakRefreshTokenProviderTests` | Cache hit/miss, the refresh buffer window, `forceRefresh` correctly bypassing a still-valid cache (including inside the double-checked lock), concurrent callers only triggering one Keycloak round-trip, and failure/exception paths |
| `SpreadsApiClientTests` | Bearer token attachment, URL construction (including id escaping), envelope success/failure mapping, the 401-forces-refresh-and-retries-once path, and malformed-response handling |

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
│       ├── InboundJwtOptions.cs
│       ├── OutboundKeycloakOptions.cs
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
│   ├── HealthChecks/
│   │   └── OutboundTokenHealthCheck.cs
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
