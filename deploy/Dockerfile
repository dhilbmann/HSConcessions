# Container build. Used by the public demo (see DEMO.md) and optional for production
# (the primary production setup is a self-contained binary under systemd; see DEPLOY.md).
#
# Build from the repo root:  docker build -f deploy/Dockerfile -t hsconcessions .
# Run with a persistent volume (data, logs, backups must survive restarts in production):
#   docker run -d -p 127.0.0.1:5080:8080 -v /var/lib/hsconcessions/myschool:/var/lib/hsconcessions \
#     --env-file /etc/hsconcessions/myschool.env hsconcessions
# The app listens on $PORT if the platform sets it (Render, Cloud Run, ...), otherwise 8080.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/HSConcessions.Web/HSConcessions.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV Storage__DataPath=/var/lib/hsconcessions/data \
    Storage__ImagesPath=/var/lib/hsconcessions/data/images \
    Storage__LogsPath=/var/lib/hsconcessions/logs \
    Storage__BackupsPath=/var/lib/hsconcessions/backups \
    DOTNET_NOLOGO=1
VOLUME ["/var/lib/hsconcessions"]
EXPOSE 8080
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet HSConcessions.Web.dll"]
