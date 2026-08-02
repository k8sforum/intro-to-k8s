# Use SDK for both stages - EF tools require SDK to run
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /s

# Copy only project files for better layer caching
COPY common/mytravels.common/*.csproj ./common/mytravels.common/
COPY common/mytravels.domain/*.csproj ./common/mytravels.domain/
COPY common/mytravels.contract/*.csproj ./common/mytravels.contract/
COPY common/mytravels.migration/*.csproj  ./common/mytravels.migration/

# Restore dependencies
RUN dotnet restore common/mytravels.migration/mytravels.migration.csproj

# Copy everything else and build
COPY . ./
RUN dotnet publish common/mytravels.migration/ -c Release -o /out/core

# Stage 2: Use SDK for runtime as well (EF tools require SDK)
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine  AS runtime
WORKDIR /s

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Copy published outputs from build stage
COPY --from=build /out/core ./core/

# Copy source code for EF tool to work
COPY --from=build /s ./src/

WORKDIR /s/src