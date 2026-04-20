FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/IdiotProof.Frontend/IdiotProof.Frontend.csproj", "src/IdiotProof.Frontend/"]
COPY ["src/IdiotProof.Engine/IdiotProof.Engine.csproj", "src/IdiotProof.Engine/"]
COPY ["src/IdiotProof.Models/IdiotProof.Models.csproj", "src/IdiotProof.Models/"]
COPY ["src/IdiotProof.Brokers/IdiotProof.Brokers.csproj", "src/IdiotProof.Brokers/"]
COPY ["src/IdiotProof.DataFeeds/IdiotProof.DataFeeds.csproj", "src/IdiotProof.DataFeeds/"]
COPY ["src/IdiotProof.Strategies/IdiotProof.Strategies.csproj", "src/IdiotProof.Strategies/"]
COPY ["src/IdiotProof.Indicators/IdiotProof.Indicators.csproj", "src/IdiotProof.Indicators/"]
COPY ["IdiotProof.Shared/IdiotProof.Shared.csproj", "IdiotProof.Shared/"]
COPY ["IdiotProof.Scripting/IdiotProof.Scripting.csproj", "IdiotProof.Scripting/"]

RUN dotnet restore "src/IdiotProof.Frontend/IdiotProof.Frontend.csproj"

COPY . .
WORKDIR "/src/src/IdiotProof.Frontend"
RUN dotnet build "IdiotProof.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "IdiotProof.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN mkdir -p /app/AppData/Settings /app/AppData/Workspaces /app/AppData/Logs /app/AppData/Data

ENTRYPOINT ["dotnet", "IdiotProof.Frontend.dll"]
