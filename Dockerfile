FROM node:24-alpine AS frontend
WORKDIR /client
RUN npm install --global pnpm@11.25.0
COPY client/package.json client/pnpm-lock.yaml client/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY client/ ./
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /source
COPY src/Roam.Core/Roam.Core.csproj src/Roam.Core/
COPY src/Roam.Api/Roam.Api.csproj src/Roam.Api/
RUN dotnet restore src/Roam.Api/Roam.Api.csproj
COPY src/ src/
COPY --from=frontend /client/dist/ src/Roam.Api/wwwroot/
RUN dotnet publish src/Roam.Api/Roam.Api.csproj --configuration Release --no-restore --output /published

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install --yes --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=backend /published/ ./
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Roam.Api.dll"]
