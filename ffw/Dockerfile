# Unter https://aka.ms/customizecontainer erfahren Sie, wie Sie Ihren Debugcontainer anpassen und wie Visual Studio dieses Dockerfile verwendet, um Ihre Images für ein schnelleres Debuggen zu erstellen.

# Diese Stufe wird verwendet, wenn sie von VS im Schnellmodus ausgeführt wird (Standardeinstellung für Debugkonfiguration).
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base
# Verzeichnisse für hochgeladene Fotos und Dokumente: müssen dem Container-Benutzer gehören, damit das benannte Volume beschreibbar ist.
RUN mkdir -p /app/data/fotos /app/data/dokumente /app/data/keys && chown -R $APP_UID /app/data
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# Diese Stufe wird zum Erstellen des Dienstprojekts verwendet.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ffw/ffw.csproj", "ffw/"]
RUN dotnet restore "./ffw/ffw.csproj"
COPY . .
WORKDIR "/src/ffw"
# Die Six-Labors-Lizenz (ImageSharp) kommt als Build-Secret hinein und landet so nicht im Image.
RUN --mount=type=secret,id=sixlabors_license \
    SIXLABORS_LICENSE_KEY="$(cat /run/secrets/sixlabors_license)" \
    dotnet build "./ffw.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Diese Stufe wird verwendet, um das Dienstprojekt zu veröffentlichen, das in die letzte Phase kopiert werden soll.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=secret,id=sixlabors_license \
    SIXLABORS_LICENSE_KEY="$(cat /run/secrets/sixlabors_license)" \
    dotnet publish "./ffw.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Diese Stufe wird in der Produktion oder bei Ausführung von VS im regulären Modus verwendet (Standard, wenn die Debugkonfiguration nicht verwendet wird).
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ffw.dll"]