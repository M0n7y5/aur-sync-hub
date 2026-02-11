FROM archlinux:latest AS build

RUN pacman -Sy --noconfirm --needed \
      dotnet-sdk \
      clang \
      lld \
      zlib \
      base-devel

WORKDIR /src

COPY src/AurSync.Updater/AurSync.Updater.csproj src/AurSync.Updater/
RUN dotnet restore src/AurSync.Updater/AurSync.Updater.csproj

COPY src/AurSync.Updater/ src/AurSync.Updater/
RUN dotnet publish src/AurSync.Updater/AurSync.Updater.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained true \
      -p:PublishAot=true \
      -p:StripSymbols=true \
      -o /out

FROM archlinux:latest AS runtime-core

RUN pacman -Sy --noconfirm --needed \
      pacman-contrib && \
    rm -rf /var/cache/pacman/pkg/*

COPY --from=build /out/aur-sync-updater /usr/local/bin/aur-sync-updater
RUN chmod 0755 /usr/local/bin/aur-sync-updater

ENV PATH="/usr/local/bin:${PATH}"

FROM runtime-core AS runtime-push

RUN pacman -Sy --noconfirm --needed \
      git \
      openssh \
      rsync && \
    rm -rf /var/cache/pacman/pkg/*
