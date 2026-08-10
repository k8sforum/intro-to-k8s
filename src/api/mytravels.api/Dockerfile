FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /app

# Copy only project files first so restore is cached until a .csproj changes
COPY common/mytravels.common/*.csproj ./common/mytravels.common/
COPY common/mytravels.contract/*.csproj ./common/mytravels.contract/
COPY common/mytravels.domain/*.csproj ./common/mytravels.domain/
COPY common/mytravels.storage/*.csproj ./common/mytravels.storage/
COPY api/mytravels.api/*.csproj ./api/mytravels.api/

RUN dotnet restore api/mytravels.api/mytravels.api.csproj

COPY . .

RUN dotnet publish api/mytravels.api/mytravels.api.csproj \
      -c Release \
      --no-restore \
      -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

WORKDIR /app/api/mytravels.api/

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:5101

EXPOSE 5101

CMD ["dotnet", "mytravels.api.dll"]
