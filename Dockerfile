# syntax=docker/dockerfile:1

# ---- Stage: css -------------------------------------------------------
# Builds the Tailwind CSS bundle. wwwroot/css/site.css is gitignored
# (generated output), so the image has to produce it itself.
FROM node:22-alpine AS css
WORKDIR /src/MedHistory

# Pin pnpm via corepack instead of installing it separately.
RUN corepack enable && corepack prepare pnpm@11.15.0 --activate

COPY MedHistory/package.json MedHistory/pnpm-lock.yaml MedHistory/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY MedHistory/Styles ./Styles
COPY MedHistory/Views ./Views
RUN pnpm run css

# ---- Stage: build -------------------------------------------------------
# Restores and publishes the ASP.NET Core app. The test project is part of
# the solution but is never published into the runtime image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project/solution files first so `dotnet restore` layer-caches
# independently of source edits.
COPY med-history.sln ./
COPY MedHistory/MedHistory.csproj MedHistory/
COPY MedHistory.Tests/MedHistory.Tests.csproj MedHistory.Tests/
RUN dotnet restore med-history.sln

COPY MedHistory/ MedHistory/
COPY MedHistory.Tests/ MedHistory.Tests/

# Must land before `dotnet publish` runs: MapStaticAssets() builds its
# manifest (and anonymous-access allowlist) from wwwroot as it exists at
# publish time. Copying the CSS in afterwards leaves it out of the
# manifest, so requests for it fall through to routing and hit the
# authenticated-by-default fallback policy instead of being served.
COPY --from=css /src/MedHistory/wwwroot/css/site.css MedHistory/wwwroot/css/site.css

RUN dotnet publish MedHistory/MedHistory.csproj -c Release -o /app/publish --no-restore

# ---- Stage: final -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# The aspnet base image ships a non-root "app" user (uid 64198 as of the
# .NET 8+ images) — run as that instead of root.
USER app

# App renders via TimeZoneInfo.Local; Cloud Run's containers default to UTC.
# The aspnet base image is Debian and ships tzdata, so setting TZ is enough
# to make local time Bangkok without installing anything extra.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Bangkok
EXPOSE 8080

ENTRYPOINT ["dotnet", "MedHistory.dll"]
