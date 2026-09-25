FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/IndirimTakip.Api/IndirimTakip.Api.csproj IndirimTakip.Api/
COPY src/IndirimTakip.Core/IndirimTakip.Core.csproj IndirimTakip.Core/
COPY src/IndirimTakip.Infrastructure/IndirimTakip.Infrastructure.csproj IndirimTakip.Infrastructure/
RUN dotnet restore IndirimTakip.Api/IndirimTakip.Api.csproj

COPY src/ .
RUN dotnet publish IndirimTakip.Api/IndirimTakip.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Tesseract OCR reads nutrition label images for free on this server (see
# TesseractLabelReader). Installed before the app is copied so this layer stays
# cached across deploys. English language data only: US labels.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# RUNS WITHOUT ROOT (security review, 2026-09-26). The backend processes
# third-party HTML and images (ImageSharp, Tesseract); a flaw in one of those
# parsers hands over the unprivileged `app` user (ready in the aspnet image,
# UID 1654) instead of root.
# The only place written to is the image directory. Giving it to `app` here
# carries the right ownership into a NEW volume (Docker fills an empty volume
# from the image's directory); the EXISTING volumes were chowned once by hand.
RUN mkdir -p /app/product-images && chown $APP_UID:$APP_UID /app/product-images
USER $APP_UID

# Render (ve benzeri platformlar) PORT ortam değişkenini kendisi atıyor —
# varsa onu kullan, yoksa 8080'e düş.
ENV PORT=8080
EXPOSE 8080

# appsettings.json'ı izleyen FileSystemWatcher, Render'ın konteyner dosya
# sisteminde native bir çökmeye (SIGABRT / status 134) yol açıyor — bu,
# kısıtlı/overlay dosya sistemli konteyner ortamlarında bilinen bir .NET
# sorunu. Config dosyalarını "değişince otomatik yenile" özelliğini
# kapatarak dosya izleyicisinin hiç kurulmasını engelliyoruz (biz zaten
# canlıda appsettings.json'ı çalışırken değiştirmiyoruz).
ENV DOTNET_hostBuilder__reloadConfigOnChange=false
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT} dotnet IndirimTakip.Api.dll"]
