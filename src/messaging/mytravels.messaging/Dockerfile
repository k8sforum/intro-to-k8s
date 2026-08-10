FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# BuildKit supplies TARGETARCH (amd64 / arm64). Publishing against an explicit
# RID makes Magick.NET emit only the matching native library instead of all
# eight it ships (~226 MB of win/osx/musl binaries that never get used).
ARG TARGETARCH

WORKDIR /app

# Copy only project files for better layer caching
COPY common/mytravels.common/*.csproj ./common/mytravels.common/
COPY common/mytravels.contract/*.csproj ./common/mytravels.contract/
COPY common/mytravels.domain/*.csproj ./common/mytravels.domain/
COPY common/mytravels.storage/*.csproj ./common/mytravels.storage/
COPY messaging/mytravels.messaging/*.csproj ./messaging/mytravels.messaging/

RUN RID="linux-$(test "$TARGETARCH" = arm64 && echo arm64 || echo x64)" \
    && dotnet restore messaging/mytravels.messaging/mytravels.messaging.csproj -r "$RID"

COPY . .

RUN RID="linux-$(test "$TARGETARCH" = arm64 && echo arm64 || echo x64)" \
    && dotnet publish messaging/mytravels.messaging/mytravels.messaging.csproj \
         -c Release \
         -r "$RID" \
         --self-contained false \
         --no-restore \
         -o /app/publish

# Stays on the glibc (Debian) base: Magick.NET has no linux-musl-arm64 native
# library, so an Alpine runtime would break the arm64 build.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app/messaging/mytravels.messaging/

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:5102

EXPOSE 5102

CMD ["dotnet", "mytravels.messaging.dll"]
