# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SuperMod.sln ./
COPY src/SuperMod/SuperMod.csproj src/SuperMod/
RUN dotnet restore src/SuperMod/SuperMod.csproj
COPY src/ src/
RUN dotnet publish src/SuperMod/SuperMod.csproj -c Release -o /app

# Run
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Configure via environment variables, e.g.:
#   -e SuperMod__DiscordToken=...  -e SuperMod__Ai__Provider=ollama  -e SuperMod__Ai__Model=llama3.1
ENTRYPOINT ["dotnet", "SuperMod.dll"]
