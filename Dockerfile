FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY src/ src/
COPY tests/ tests/
COPY docs/loyverse-schema.sql docs/loyverse-schema.sql
COPY RaLaba.sln ./
RUN dotnet test tests/RaLaba.UnitTests/RaLaba.UnitTests.csproj --configuration Release
RUN dotnet publish src/RaLaba.API/RaLaba.API.csproj --configuration Release --output /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production
USER app
EXPOSE 10000
CMD ["/bin/sh", "-c", "exec dotnet RaLaba.API.dll --urls http://0.0.0.0:${PORT:-10000}"]
