FROM mcr.microsoft.com/dotnet/sdk:10.0.300@sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6 AS build
WORKDIR /src
COPY AgentPing.sln Directory.Build.props global.json ./
COPY bridge/AgentPing.Bridge/AgentPing.Bridge.csproj bridge/AgentPing.Bridge/packages.lock.json bridge/AgentPing.Bridge/
COPY bridge/AgentPing.Bridge.Tests/AgentPing.Bridge.Tests.csproj bridge/AgentPing.Bridge.Tests/packages.lock.json bridge/AgentPing.Bridge.Tests/
RUN dotnet restore AgentPing.sln --locked-mode
COPY bridge/ bridge/
RUN dotnet publish bridge/AgentPing.Bridge/AgentPing.Bridge.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    && chmod --recursive a=rX /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.8@sha256:8c0b6857eab7b2aa57884c839bf4678414606bd7d17370f18a842ac5cf414711 AS runtime
WORKDIR /app
ENV Kestrel__Endpoints__Http__Url=http://0.0.0.0:8742
EXPOSE 8742
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AgentPing.Bridge.dll"]
