# Multi-stage build: the SDK image compiles, the smaller ASP.NET runtime image runs.
# Build context is the repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the NuGet layer is cached while only source files change.
COPY src/ExpenseManagement.Api/ExpenseManagement.Api.csproj src/ExpenseManagement.Api/
RUN dotnet restore src/ExpenseManagement.Api/ExpenseManagement.Api.csproj

COPY src/ExpenseManagement.Api/ src/ExpenseManagement.Api/
RUN dotnet publish src/ExpenseManagement.Api/ExpenseManagement.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# .NET 8 images listen on 8080 and provide a non-root "app" user.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "ExpenseManagement.Api.dll"]
