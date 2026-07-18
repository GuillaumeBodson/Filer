# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Kerberos/GSSAPI runtime lib: Npgsql attempts to load it during connection
# negotiation. Without it the connection still succeeds via SCRAM, but Npgsql
# logs a noisy "Cannot load library libgssapi_krb5.so.2" error on startup.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Central MSBuild inputs first: Directory.Build.props carries the TargetFramework
# and Directory.Packages.props the package versions - restore fails without them.
# Then the full project graph of Filer.Api, so this layer only invalidates when
# the dependency graph changes, not on every source edit.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/Filer.Api/Filer.Api.csproj", "src/Filer.Api/"]
COPY ["src/Filer.SharedKernel/Filer.SharedKernel.csproj", "src/Filer.SharedKernel/"]
COPY ["src/Modules/AiAnalysis/Filer.Modules.AiAnalysis/Filer.Modules.AiAnalysis.csproj", "src/Modules/AiAnalysis/Filer.Modules.AiAnalysis/"]
COPY ["src/Modules/AiAnalysis/Filer.Modules.AiAnalysis.Contracts/Filer.Modules.AiAnalysis.Contracts.csproj", "src/Modules/AiAnalysis/Filer.Modules.AiAnalysis.Contracts/"]
COPY ["src/Modules/Auth/Filer.Modules.Auth/Filer.Modules.Auth.csproj", "src/Modules/Auth/Filer.Modules.Auth/"]
COPY ["src/Modules/Auth/Filer.Modules.Auth.Contracts/Filer.Modules.Auth.Contracts.csproj", "src/Modules/Auth/Filer.Modules.Auth.Contracts/"]
COPY ["src/Modules/BackgroundJobs/Filer.Modules.BackgroundJobs/Filer.Modules.BackgroundJobs.csproj", "src/Modules/BackgroundJobs/Filer.Modules.BackgroundJobs/"]
COPY ["src/Modules/BackgroundJobs/Filer.Modules.BackgroundJobs.Contracts/Filer.Modules.BackgroundJobs.Contracts.csproj", "src/Modules/BackgroundJobs/Filer.Modules.BackgroundJobs.Contracts/"]
COPY ["src/Modules/Documents/Filer.Modules.Documents/Filer.Modules.Documents.csproj", "src/Modules/Documents/Filer.Modules.Documents/"]
COPY ["src/Modules/Documents/Filer.Modules.Documents.Contracts/Filer.Modules.Documents.Contracts.csproj", "src/Modules/Documents/Filer.Modules.Documents.Contracts/"]
COPY ["src/Modules/Folders/Filer.Modules.Folders/Filer.Modules.Folders.csproj", "src/Modules/Folders/Filer.Modules.Folders/"]
COPY ["src/Modules/Folders/Filer.Modules.Folders.Contracts/Filer.Modules.Folders.Contracts.csproj", "src/Modules/Folders/Filer.Modules.Folders.Contracts/"]
COPY ["src/Modules/Search/Filer.Modules.Search/Filer.Modules.Search.csproj", "src/Modules/Search/Filer.Modules.Search/"]
COPY ["src/Modules/Search/Filer.Modules.Search.Contracts/Filer.Modules.Search.Contracts.csproj", "src/Modules/Search/Filer.Modules.Search.Contracts/"]
COPY ["src/Modules/Storage/Filer.Modules.Storage/Filer.Modules.Storage.csproj", "src/Modules/Storage/Filer.Modules.Storage/"]
COPY ["src/Modules/Storage/Filer.Modules.Storage.Contracts/Filer.Modules.Storage.Contracts.csproj", "src/Modules/Storage/Filer.Modules.Storage.Contracts/"]
COPY ["src/Modules/Tags/Filer.Modules.Tags/Filer.Modules.Tags.csproj", "src/Modules/Tags/Filer.Modules.Tags/"]
COPY ["src/Modules/Tags/Filer.Modules.Tags.Contracts/Filer.Modules.Tags.Contracts.csproj", "src/Modules/Tags/Filer.Modules.Tags.Contracts/"]
RUN dotnet restore "./src/Filer.Api/Filer.Api.csproj"
COPY . .
WORKDIR "/src/src/Filer.Api"
RUN dotnet build "./Filer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Filer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Filer.Api.dll"]