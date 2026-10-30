# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HospitalApi.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false

# ---- run ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY --from=build /app ./
# Never run as root: the base image provides a non-root "app" user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HospitalApi.dll"]
