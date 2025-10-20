# build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ../../BenchRunner.sln ./
COPY ../../src ./src
COPY ../../tests ./tests
RUN dotnet restore src/AuthServer/AuthServer.csproj
RUN dotnet publish src/AuthServer/AuthServer.csproj -c Release -o /app/publish

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=https://+:5001
ENTRYPOINT ["dotnet", "AuthServer.dll"]
