# Build a partir da raiz de backend/, para que as referências entre projetos resolvam:
#   docker build -f backend/OpsDesk.Api/Dockerfile backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore em camada própria: só invalida o cache quando um csproj muda, não a cada
# alteração de código.
COPY OpsDesk.Api/OpsDesk.Api.csproj OpsDesk.Api/
COPY OpsDesk.Application/OpsDesk.Application.csproj OpsDesk.Application/
COPY OpsDesk.Domain/OpsDesk.Domain.csproj OpsDesk.Domain/
COPY OpsDesk.Infrastructure/OpsDesk.Infrastructure.csproj OpsDesk.Infrastructure/
RUN dotnet restore OpsDesk.Api/OpsDesk.Api.csproj

COPY . .
RUN dotnet publish OpsDesk.Api/OpsDesk.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# tzdata: o cálculo de horas úteis depende de America/Sao_Paulo existir na imagem.
# curl: a imagem do ASP.NET não traz nenhum cliente HTTP, e sem um deles o healthcheck
# do container falha com "wget: not found" enquanto a API responde normalmente.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Usuário sem privilégio, já presente nas imagens oficiais.
USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "OpsDesk.Api.dll"]
