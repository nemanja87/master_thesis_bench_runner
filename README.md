# BenchRunner Microservices Demo

A .NET 9 sample environment that exercises REST and gRPC workloads across multiple security profiles. BenchRunner drives load tests (k6 for REST, ghz for gRPC), persists metrics to PostgreSQL, and serves a React dashboard for visualising percentile and throughput trends.

## Architecture

| Service | Purpose |
| --- | --- |
| **AuthServer** | OpenIddict-based OIDC provider issuing RS256 JWT tokens. |
| **Gateway** | YARP reverse proxy that fronts REST (HTTP/1.1) and gRPC (HTTP/2) traffic, optionally enforcing TLS, mTLS, and JWT validation. |
| **OrderService** | Minimal API + gRPC worker that accepts order creation requests and exposes Prometheus metrics. |
| **InventoryService** | Lightweight dependency used by OrderService to mimic downstream calls. |
| **ResultsService** | Orchestrates BenchRunner executes, converts ghz nanosecond metrics to milliseconds, and stores history in PostgreSQL. |
| **BenchRunner** | Component inside ResultsService container that shells out to k6/ghz with profile-aware configuration. |
| **Dashboard** | Vite + React application displaying health, run history, and latency charts. |
| **Postgres** | Persists benchmark run metadata. |

Security behaviour is driven by the `SEC_PROFILE` environment variable:

- `S0`: HTTP only.
- `S1`: TLS only.
- `S2`: TLS + JWT.
- `S3`: mTLS.
- `S4`: mTLS + JWT.

Shared helpers (in `Shared`) expose `SecurityProfileDefaults` so each service reacts consistently.

### Gateway vs direct benchmarking

BenchRunner can now hit workloads either **through the Gateway** (YARP reverse proxy) or **directly against OrderService**. Use the new “Call Path” dropdown on the Run Experiments screen (or the `callPath` field in the API) to flip between `gateway` and `direct`. Environment variables under `BENCH_Target__Rest*` control the gateway endpoints, while the new `BENCH_Target__RestDirect*` and `BENCH_Target__GrpcDirectAddress` settings define the direct OrderService addresses.

## Prerequisites

- Docker Desktop (or Docker Engine + Compose v2)
- Bash and OpenSSL for certificate generation
- Node.js 20+ (optional if you want to run the dashboard locally)

## Local Browser TLS note

The dashboard UI talks to `ResultsService` over HTTP (`http://localhost:8000`) during local development. This avoids browser errors caused by self-signed certificates that lack a `localhost` subject alternate name. Backend-to-backend traffic continues to respect the selected `SEC_PROFILE` (TLS, mTLS, JWT as configured). The HTTP listener is controlled by the `RESULTS__UiScheme` setting (defaults to `http`).

## Generate development certificates

```bash
cd deploy/certs
./generate.sh
```

Outputs are placed under `deploy/certs`:

- `ca/` – root CA key/cert
- `servers/<service>/` – PFX + PEM bundles for each ASP.NET Core service
- `clients/<name>/` – PEM + PFX for client mutual TLS (bench-runner and results-service)

## Build & run with Docker Compose

```bash
cd deploy
export SEC_PROFILE=S2               # choose S0–S4
./certs/generate.sh                 # only required the first time
SEC_PROFILE=$SEC_PROFILE docker compose up -d --build
```

Services listen on the following host ports:

- AuthServer – `https://localhost:5001`
- Gateway – `http://localhost:8080`, `https://localhost:9090`
- OrderService – `http://localhost:8081`, `https://localhost:9091`
- InventoryService – `http://localhost:8082`, `https://localhost:9092`
- ResultsService API – `http://localhost:8000`
- Dashboard – `http://localhost:4173`

### Acceptance flow (S2 → S4)

1. **Token acquisition (inside ResultsService container)**

   ```bash
   cd deploy
   docker compose exec resultsservice sh -c '
   RESP=$(mktemp)
   curl -s -o "$RESP" \
     --cacert /certs/ca/ca.crt.pem \
     -H "Content-Type: application/x-www-form-urlencoded" \
     --data-urlencode grant_type=client_credentials \
     --data-urlencode client_id=bench-runner \
     --data-urlencode client_secret=bench-runner-secret \
     --data-urlencode scope="orders.write orders.read inventory.write" \
     https://authserver:5001/connect/token
   TOKEN=$(tr -d "\r\n" < "$RESP" | sed "s/.*\"access_token\":\"\([^\"]*\)\".*/\1/")
   echo "token.len=${#TOKEN}"
   '
   ```

2. **REST smoke through gateway (S2)**

   ```bash
   docker compose exec resultsservice sh -c '
   TOKEN=$(curl -s \
     --cacert /certs/ca/ca.crt.pem \
     -H "Content-Type: application/x-www-form-urlencoded" \
     --data-urlencode grant_type=client_credentials \
     --data-urlencode client_id=bench-runner \
     --data-urlencode client_secret=bench-runner-secret \
     --data-urlencode scope="orders.write orders.read inventory.write" \
     https://authserver:5001/connect/token \
     | tr -d "\r\n" | sed "s/.*\"access_token\":\"\([^\"]*\)\".*/\1/")
   curl -i \
     --cacert /certs/ca/ca.crt.pem \
     -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"customerId":"cust-1","itemSkus":["sku-1"],"totalAmount":42.0}' \
     https://gateway:9090/orders/api/orders | sed -n "1,80p"
   '
   ```

3. **Trigger benchmark**

   ```bash
   curl -s -X POST "http://localhost:8000/api/benchrunner/run" \
     -H "Content-Type: application/json" \
     -d '{"protocol":"grpc","security":"S2","workload":"orders-create","rps":10,"duration":5,"warmup":1,"connections":1}' | jq .

   docker compose logs --since=5m resultsservice | sed -n '/normalized to ms/p'
   ```

4. **Switch to S4**

   ```bash
   docker compose down
   SEC_PROFILE=S4 docker compose up -d --build gateway resultsservice
   # repeat steps 1–3
   ```

5. **Dashboard** – open [http://localhost:4173](http://localhost:4173) to submit runs, view percentiles, and inspect history.

## Local development

- **.NET build & tests**

  ```bash
  dotnet build BenchRunner.sln
  dotnet test BenchRunner.sln
  ```
- **ResultsService tests only**

  ```bash
  dotnet test tests/ResultsService.Tests/ResultsService.Tests.csproj
  ```
- **React dashboard**

  ```bash
  cd ui/dashboard
  npm install
  npm run dev
  # open http://localhost:5173
  ```

  Configure the API base via `.env` (`VITE_RESULTS_API_BASE=http://localhost:8000`).

## Prometheus scraping

OrderService exposes `/metrics` (Prometheus format) on its HTTP endpoint, enabling optional integration with Prometheus/Grafana stacks.

## Full TLS to localhost

If you prefer the browser to call `ResultsService` over HTTPS, generate a certificate with SAN entries for `localhost` and `resultsservice`:

```bash
make cert-resultsservice-localhost
cd deploy
RESULTS_UI_SCHEME=https SEC_PROFILE=S2 docker compose up -d --build resultsservice
curl --cacert ../deploy/certs/ca/ca.crt.pem https://localhost:8000/healthz
```

Then update the dashboard API base to `https://localhost:8000` (for example by editing `ui/dashboard/.env.local`) and trust `deploy/certs/ca/ca.crt.pem` in your browser/OS.

## Configuration summary

Key environment knobs:

| Variable | Description |
| --- | --- |
| `SEC_PROFILE` | Security posture selector (S0–S4). |
| `BENCH_Target__*` | ResultsService base URLs for REST/gRPC benchmarks. |
| `BENCH_Target__RestDirect*` | Direct-to-OrderService REST endpoints used when `callPath=direct`. |
| `BENCH_Target__GrpcDirectAddress` | Direct-to-OrderService gRPC endpoint for `callPath=direct`. |
| `BENCH_Security__Jwt__*` | Token endpoint configuration for BenchRunner. |
| `BENCH_Security__Tls__*` | Paths to CA/client/server certificates. |
| `RESULTS__AllowAnonymousReads` | Enable anonymous GET access to run history. |

See `deploy/docker-compose.yml` for the full set used in containers.

## Cleanup

To stop and remove containers/volumes:

```bash
cd deploy
docker compose down --volumes
```

To regenerate certificates, remove `deploy/certs` and rerun the generator.
