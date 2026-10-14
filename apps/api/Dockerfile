FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY apps/api/api.csproj apps/api/
RUN dotnet restore apps/api/api.csproj
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD curl --fail --silent http://localhost:8080/health || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "api.dll"]
