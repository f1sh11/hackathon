FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server-dotnet/SydneyBuddy.Api/SydneyBuddy.Api.csproj server-dotnet/SydneyBuddy.Api/
RUN dotnet restore server-dotnet/SydneyBuddy.Api/SydneyBuddy.Api.csproj
COPY server-dotnet/SydneyBuddy.Api/ server-dotnet/SydneyBuddy.Api/
RUN dotnet publish server-dotnet/SydneyBuddy.Api/SydneyBuddy.Api.csproj -c Release --no-restore -o /out

FROM node:24-bookworm-slim AS node
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=build /out/ ./
COPY --chown=app:app server/ ./server/
COPY --chown=app:app shared/ ./shared/
COPY --chown=app:app infra/ ./infra/
RUN mkdir -p /var/lib/sydneybuddy && chown app:app /var/lib/sydneybuddy
ENV SYDNEYBUDDY_ROOT=/app SYDNEYBUDDY_DATA=/var/lib/sydneybuddy NODE_BINARY=/usr/local/bin/node
ENV Kestrel__Endpoints__Http__Url=http://0.0.0.0:8787
USER app
EXPOSE 8787
ENTRYPOINT ["dotnet", "SydneyBuddy.Api.dll"]
