# syntax=docker/dockerfile:1

# ---- build -------------------------------------------------------------------------------
# Restore before copying the rest of the source, so a change to a .cs file does not throw
# away the NuGet layer. The project files are the only thing restore reads.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .editorconfig is part of the build, not an editor preference. EnforceCodeStyleInBuild is
# on, and the section marking EF's generated migrations as generated code lives in it —
# without the file, the analyzers fail the publish on scaffolded code.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/StatusPage.Domain/StatusPage.Domain.csproj src/StatusPage.Domain/
COPY src/StatusPage.Infrastructure/StatusPage.Infrastructure.csproj src/StatusPage.Infrastructure/
COPY src/StatusPage.Api/StatusPage.Api.csproj src/StatusPage.Api/
RUN dotnet restore src/StatusPage.Api/StatusPage.Api.csproj

COPY src/ src/
RUN dotnet publish src/StatusPage.Api/StatusPage.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# ---- runtime -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The image ships with a non-root user. Running as root inside a container buys nothing here:
# the app opens one port, reads one connection string and writes to blob storage.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080

COPY --from=build /app .

# Exec form, so dotnet is PID 1 and receives SIGTERM directly. The shell form would put a
# shell there instead, and the platform's polite shutdown would become a kill after a
# timeout — which is how requests get dropped during a rolling deploy.
ENTRYPOINT ["dotnet", "StatusPage.Api.dll"]
