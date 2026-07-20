ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled@sha256:70d6f993bf715a031f027832a19cfb7f894df66c8b5eb40be0aaee820ad5d119
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.302-noble@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664

FROM ${SDK_IMAGE} AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "./Services/Feed/Feed.Api/Feed.Api.csproj" --locked-mode \
 && dotnet restore "./DevOps/HealthProbe/HealthProbe.csproj" --locked-mode
RUN dotnet publish "./Services/Feed/Feed.Api/Feed.Api.csproj" -c $BUILD_CONFIGURATION --no-restore -o /out/app /p:UseAppHost=false
RUN dotnet publish "./DevOps/HealthProbe/HealthProbe.csproj" -c $BUILD_CONFIGURATION --no-restore -o /out/health /p:UseAppHost=false

FROM ${ASPNET_IMAGE} AS final
ARG VCS_REF=unknown
ARG BUILD_DATE=unknown
LABEL org.opencontainers.image.title="Inspacy Feed API" \
      org.opencontainers.image.revision=$VCS_REF \
      org.opencontainers.image.created=$BUILD_DATE
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /out/app .
COPY --from=build --chown=$APP_UID:$APP_UID /out/health /health
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD ["dotnet", "/health/HealthProbe.dll", "http://127.0.0.1:8080/health/live"]
ENTRYPOINT ["dotnet", "Feed.Api.dll"]
