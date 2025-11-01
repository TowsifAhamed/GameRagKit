FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY GameRagKit.sln ./
COPY src ./src
COPY docs ./docs
COPY samples ./samples
COPY docker ./docker
RUN dotnet restore GameRagKit.sln
RUN dotnet publish src/GameRagKit.Cli/GameRagKit.Cli.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:5280
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "GameRagKit.Cli.dll", "serve", "--config", "/app/config", "--port", "5280"]
