# Stage 1: build the SPA
FROM node:20-alpine AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# Stage 2: build and publish the API
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /src
COPY MekomonPublisher.Api/*.csproj ./MekomonPublisher.Api/
RUN dotnet restore ./MekomonPublisher.Api/MekomonPublisher.Api.csproj
COPY MekomonPublisher.Api/ ./MekomonPublisher.Api/
RUN dotnet publish ./MekomonPublisher.Api/MekomonPublisher.Api.csproj -c Release -o /app/publish --no-restore

# Stage 3: runtime - one small image, API + SPA together, same origin
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api-build /app/publish ./
COPY --from=web-build /web/dist ./wwwroot

# The base image already listens on 8080 by default (ASPNETCORE_HTTP_PORTS);
# EXPOSE just lets the host platform auto-detect it.
EXPOSE 8080

# SQLite lives here; mount a persistent volume at this path if your host
# supports one, otherwise history resets on every redeploy (harmless -
# WordPress stays the source of truth for the articles themselves).
RUN mkdir -p /app/App_Data
VOLUME ["/app/App_Data"]

ENTRYPOINT ["dotnet", "MekomonPublisher.Api.dll"]
