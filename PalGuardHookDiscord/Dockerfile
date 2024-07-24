# Use uma imagem base do .NET para a aplicação final
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
RUN apt-get update && apt-get install -y cifs-utils

# Imagem base para o build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PalGuardHookDiscord.csproj", "./"]
RUN dotnet restore "./PalGuardHookDiscord.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "PalGuardHookDiscord.csproj" -c Release -o /app/build

# Publicação da aplicação
FROM build AS publish
RUN dotnet publish "PalGuardHookDiscord.csproj" -c Release -o /app/publish

# Imagem final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]