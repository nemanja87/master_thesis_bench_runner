# build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ../../BenchRunner.sln ./
COPY ../../src ./src
COPY ../../tests ./tests
RUN dotnet restore src/ResultsService/ResultsService.csproj
RUN dotnet publish src/ResultsService/ResultsService.csproj -c Release -o /app/publish

# ghz build stage
ARG GHZ_VERSION=0.120.0
FROM golang:1.22 AS ghz-build
ARG GHZ_VERSION=0.120.0
ARG TARGETARCH
ENV CGO_ENABLED=0
RUN --mount=type=cache,target=/root/.cache/go-build \
    --mount=type=cache,target=/go/pkg/mod \
    GOOS=linux GOARCH="${TARGETARCH:-amd64}" \
    go install github.com/bojand/ghz/cmd/ghz@v${GHZ_VERSION}

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
ARG K6_VERSION=0.49.0
ARG TARGETARCH
WORKDIR /app
RUN set -eux; \
    case "$TARGETARCH" in \
        "amd64") K6_SUFFIX="linux-amd64" ;; \
        "arm64") K6_SUFFIX="linux-arm64" ;; \
        *) echo "Unsupported TARGETARCH=$TARGETARCH" >&2; exit 1 ;; \
    esac; \
    apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates tar \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL "https://github.com/grafana/k6/releases/download/v${K6_VERSION}/k6-v${K6_VERSION}-${K6_SUFFIX}.tar.gz" | tar -xz -C /tmp \
    && mv "/tmp/k6-v${K6_VERSION}-${K6_SUFFIX}/k6" /usr/local/bin/k6 \
    && rm -rf "/tmp/k6-v${K6_VERSION}-${K6_SUFFIX}" \
    && chmod +x /usr/local/bin/k6
COPY --from=build /app/publish .
COPY --from=ghz-build /go/bin/ghz /usr/local/bin/ghz
COPY ../../src/Shared.Contracts/Protos/ordering.proto /app/protos/ordering.proto
RUN chmod +x /usr/local/bin/ghz /usr/local/bin/k6
ENV ASPNETCORE_URLS=http://+:8000
ENTRYPOINT ["dotnet", "ResultsService.dll"]
