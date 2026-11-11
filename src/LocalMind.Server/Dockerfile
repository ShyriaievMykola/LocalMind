# Сервер бекапу LocalMind. Збирати з кореня репозиторію:
#   docker build -f src/LocalMind.Server/Dockerfile -t localmind-server .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Спершу лише файли проєктів: шар restore кешується, поки не змінились залежності.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LocalMind.Core/LocalMind.Core.csproj src/LocalMind.Core/
COPY src/LocalMind.Server/LocalMind.Server.csproj src/LocalMind.Server/
RUN dotnet restore src/LocalMind.Server/LocalMind.Server.csproj

COPY src/LocalMind.Core/ src/LocalMind.Core/
COPY src/LocalMind.Server/ src/LocalMind.Server/
RUN dotnet publish src/LocalMind.Server/LocalMind.Server.csproj \
    -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Дані — в окремих теках під томи; працюємо не від root (користувач app є в образі).
RUN mkdir -p /data/db /data/backups && chown -R app:app /data
USER app

# Усередині контейнера — лише HTTP: TLS завершує reverse proxy (див. README, ReverseProxy__*).
# Секретів в образі немає: ключ JWT (Jwt__SigningKey) передається змінною середовища під час запуску,
# без нього сервер не стартує.
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Server="Data Source=/data/db/localmind-server.db" \
    Storage__RootPath=/data/backups
EXPOSE 8080
VOLUME ["/data/db", "/data/backups"]

ENTRYPOINT ["dotnet", "LocalMind.Server.dll"]
