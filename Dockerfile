# Build stage: restore first so the package layer is cached between code changes
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MaltasGarage.sln Directory.Build.props ./
COPY src/MaltasGarage.Domain/MaltasGarage.Domain.csproj src/MaltasGarage.Domain/packages.lock.json src/MaltasGarage.Domain/
COPY src/MaltasGarage.Application/MaltasGarage.Application.csproj src/MaltasGarage.Application/packages.lock.json src/MaltasGarage.Application/
COPY src/MaltasGarage.Infrastructure/MaltasGarage.Infrastructure.csproj src/MaltasGarage.Infrastructure/packages.lock.json src/MaltasGarage.Infrastructure/
COPY src/MaltasGarage.Web/MaltasGarage.Web.csproj src/MaltasGarage.Web/packages.lock.json src/MaltasGarage.Web/
# Same publish settings as CI (ci-cd.yml): framework-dependent linux-x64 with ReadyToRun
RUN dotnet restore src/MaltasGarage.Web/MaltasGarage.Web.csproj --runtime linux-x64 -p:PublishReadyToRun=true

COPY src/ src/
RUN dotnet publish src/MaltasGarage.Web/MaltasGarage.Web.csproj \
    --configuration Release --no-restore --runtime linux-x64 --self-contained false \
    -p:PublishReadyToRun=true --output /app

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Uploaded images live outside the app folder so a volume can keep them
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__Provider=Local \
    Storage__LocalRootPath=/data/uploads
RUN mkdir -p /data/uploads && chown -R $APP_UID /data
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "MaltasGarage.Web.dll"]
