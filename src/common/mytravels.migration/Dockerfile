# Stage 1: build a self-contained EF migrations bundle.
# The bundle is a single executable that applies migrations and exits, so the
# runtime stage needs neither the .NET SDK nor a copy of the source tree.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /s

# Copy only project files for better layer caching
COPY .config/dotnet-tools.json ./.config/
COPY common/mytravels.common/*.csproj ./common/mytravels.common/
COPY common/mytravels.domain/*.csproj ./common/mytravels.domain/
COPY common/mytravels.contract/*.csproj ./common/mytravels.contract/
COPY common/mytravels.migration/*.csproj  ./common/mytravels.migration/

# Restore dependencies and the pinned dotnet-ef tool
RUN dotnet tool restore
RUN dotnet restore common/mytravels.migration/mytravels.migration.csproj

# Copy everything else and build the bundle
COPY . ./
RUN dotnet ef migrations bundle \
      --configuration Release \
      --project common/mytravels.domain \
      --startup-project common/mytravels.migration \
      --context CoreDbContext \
      --output /out/efbundle

# Stage 2: runtime only. The bundle carries the migrations, so the SDK
# (~1 GB with its NuGet cache and PowerShell) never reaches the final image.
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime

# The Alpine .NET images default to invariant globalization and ship no ICU
# data; Npgsql needs it for collation-aware comparisons.
RUN apk add --no-cache icu-data-full icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app

COPY --from=build /out/efbundle ./efbundle
COPY --from=build /s/common/mytravels.migration/appsettings.json ./

# The UpdateStoredProcedure migration loads these scripts from
# AppContext.BaseDirectory at apply time, so they must sit next to the bundle.
# (SeedData looks for a SeedScripts/ directory but returns early when absent.)
COPY --from=build /s/common/mytravels.domain/Features ./Features/

# Pass the target database as: --connection "<connection string>"
ENTRYPOINT ["./efbundle"]
