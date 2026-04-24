FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /app

COPY . .

WORKDIR /app/api/mytravels.api/
RUN dotnet restore

RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

WORKDIR /app/api/mytravels.api/

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:5101

EXPOSE 5101

CMD ["dotnet", "mytravels.api.dll"]