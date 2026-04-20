FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

ARG GITHUB_ACTOR

COPY nuget.config ./
COPY MaichessUserService/MaichessUserService.csproj MaichessUserService/
RUN --mount=type=secret,id=GITHUB_TOKEN \
    GITHUB_TOKEN=$(cat /run/secrets/GITHUB_TOKEN) \
    dotnet restore MaichessUserService/MaichessUserService.csproj

COPY MaichessUserService/ MaichessUserService/
RUN dotnet publish MaichessUserService/MaichessUserService.csproj \
    -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MaichessUserService.dll"]
