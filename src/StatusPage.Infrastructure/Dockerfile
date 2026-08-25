# syntax=docker/dockerfile:1

# The migrator. A self-contained bundle of the migrations, produced by `dotnet ef migrations
# bundle`, in an image with no SDK and no EF tooling in it.
#
# It is a separate image because migrating from the application's own startup is a race the
# moment there is more than one replica: two instances race to take the same lock, and the
# loser either waits or fails depending on the provider's mood. A one-shot job that must
# finish before the app starts has neither problem, and it also means a failed migration
# stops the deployment instead of crash-looping a container.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/StatusPage.Domain/StatusPage.Domain.csproj src/StatusPage.Domain/
COPY src/StatusPage.Infrastructure/StatusPage.Infrastructure.csproj src/StatusPage.Infrastructure/
RUN dotnet restore src/StatusPage.Infrastructure/StatusPage.Infrastructure.csproj

COPY src/ src/

RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"

RUN dotnet ef migrations bundle \
    --project src/StatusPage.Infrastructure \
    --configuration Release \
    --self-contained \
    --target-runtime linux-x64 \
    --output /app/migrate \
    --force

# ---- runtime -----------------------------------------------------------------------------
# The bundle is self-contained, so this needs no .NET at all. It still needs ICU, because
# Microsoft.Data.SqlClient refuses to start in globalization-invariant mode.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app

USER $APP_UID

COPY --from=build /app/migrate ./migrate

ENTRYPOINT ["./migrate"]
