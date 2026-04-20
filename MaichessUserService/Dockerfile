FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG GITHUB_TOKEN
ARG GITHUB_ACTOR=maichess

WORKDIR /src

# NuGet reads %GITHUB_TOKEN% / %GITHUB_ACTOR% from the environment
ENV GITHUB_TOKEN=${GITHUB_TOKEN}
ENV GITHUB_ACTOR=${GITHUB_ACTOR}

COPY nuget.config ./
COPY MaichessUserService/MaichessUserService.csproj MaichessUserService/
RUN dotnet restore MaichessUserService/MaichessUserService.csproj

COPY MaichessUserService/ MaichessUserService/
RUN dotnet publish MaichessUserService/MaichessUserService.csproj \
    -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MaichessUserService.dll"]
