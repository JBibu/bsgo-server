# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore separate from the code: the dependency layer is cached across builds.
COPY Bsgo.sln .
# Carries TargetFramework and friends: without it the projects below build
# against nothing. The .editorconfig comes along because the props file turns
# its rules into build warnings, and the image should build under the same
# rules as the developer's machine.
COPY Directory.Build.props .editorconfig ./
COPY src/Bsgo.Protocol/Bsgo.Protocol.csproj src/Bsgo.Protocol/
COPY src/Bsgo.Server/Bsgo.Server.csproj src/Bsgo.Server/
COPY tests/Bsgo.Protocol.Tests/Bsgo.Protocol.Tests.csproj tests/Bsgo.Protocol.Tests/
COPY tests/Bsgo.Server.Tests/Bsgo.Server.Tests.csproj tests/Bsgo.Server.Tests/
RUN dotnet restore src/Bsgo.Server/Bsgo.Server.csproj

COPY src/ src/
COPY data/ data/
RUN dotnet publish src/Bsgo.Server/Bsgo.Server.csproj -c Release -o /app --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# No root: the server needs no privileges.
USER $APP_UID

EXPOSE 27050
ENTRYPOINT ["dotnet", "Bsgo.Server.dll"]
