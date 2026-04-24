FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /app

COPY . .

WORKDIR /app/messaging/mytravels.messaging/

RUN dotnet restore

RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app/messaging/mytravels.messaging/

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:5102

EXPOSE 5102

CMD ["dotnet", "mytravels.messaging.dll"]
