# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .editorconfig is part of the build, not an editor preference. EnforceCodeStyleInBuild is
# on, and the section marking EF's generated migrations as generated code lives in it —
# without the file, the analyzers fail the publish on scaffolded code.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/StatusPage.Domain/StatusPage.Domain.csproj src/StatusPage.Domain/
COPY src/StatusPage.Infrastructure/StatusPage.Infrastructure.csproj src/StatusPage.Infrastructure/
COPY src/StatusPage.Checker/StatusPage.Checker.csproj src/StatusPage.Checker/
RUN dotnet restore src/StatusPage.Checker/StatusPage.Checker.csproj

COPY src/ src/
RUN dotnet publish src/StatusPage.Checker/StatusPage.Checker.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# ---- runtime -----------------------------------------------------------------------------
# The runtime image, not aspnet: the checker serves nothing and listens on no port. It runs,
# writes what changed, and exits.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

USER $APP_UID

COPY --from=build /app .

# No HEALTHCHECK and no EXPOSE. A process that is supposed to exit has no health to report,
# and a scheduler that restarts it on a non-zero exit is the whole supervision it needs.
ENTRYPOINT ["dotnet", "StatusPage.Checker.dll"]
