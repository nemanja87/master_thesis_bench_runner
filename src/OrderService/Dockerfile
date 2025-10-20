FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ../../BenchRunner.sln ./
COPY ../../src ./src
COPY ../../tests ./tests
RUN dotnet restore src/OrderService/OrderService.csproj
RUN dotnet publish src/OrderService/OrderService.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderService.dll"]
