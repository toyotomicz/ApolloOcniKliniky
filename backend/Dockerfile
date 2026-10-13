# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config global.json ./
COPY src/ApolloData.Api/ApolloData.Api.csproj src/ApolloData.Api/
RUN dotnet restore src/ApolloData.Api/ApolloData.Api.csproj
COPY src/ src/
RUN dotnet publish src/ApolloData.Api/ApolloData.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DATA_PROTECTION_KEYS_PATH=/data/keys
# Directory for the data-protection key ring (cookie encryption keys), mounted as a named volume.
RUN mkdir -p /data/keys && chown "$APP_UID" /data/keys
COPY --from=build /app/publish .
# Non-root user provided by the official .NET images.
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=40s --retries=6 \
    CMD ["dotnet", "ApolloData.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "ApolloData.Api.dll"]
