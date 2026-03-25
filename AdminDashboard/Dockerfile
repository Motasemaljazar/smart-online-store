# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AdminDashboard.csproj .
RUN dotnet restore "AdminDashboard.csproj"

COPY . .
RUN dotnet build "AdminDashboard.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "AdminDashboard.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

RUN groupadd -r appuser && useradd -r -g appuser appuser

WORKDIR /app

COPY --from=publish /app/publish .

# تأكد من وجود مجلد wwwroot لرفع الملفات
RUN mkdir -p /app/wwwroot/uploads && chown -R appuser:appuser /app

USER appuser

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "AdminDashboard.dll"]
