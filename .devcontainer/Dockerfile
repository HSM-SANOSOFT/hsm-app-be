# Build context: monorepo root

FROM node:24-bookworm-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    bash git curl libaio1 libnsl2 ca-certificates \
    fonts-liberation libatk-bridge2.0-0 libatk1.0-0 libcups2 \
    libdbus-1-3 libdrm2 libgbm1 libnspr4 libnss3 libx11-6 libxext6 \
  && rm -rf /var/lib/apt/lists/*

COPY docker/oracle/instantclient_12_1 /usr/lib/oracle/12.1/client64/lib/
RUN ln -sfn /usr/lib/oracle/12.1/client64/lib/libclntsh.so.12.1 \
            /usr/lib/oracle/12.1/client64/lib/libclntsh.so || true
RUN echo "/usr/lib/oracle/12.1/client64/lib" \
      > /etc/ld.so.conf.d/oracle-instantclient.conf && ldconfig

RUN corepack enable
