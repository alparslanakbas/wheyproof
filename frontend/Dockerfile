FROM node:24.19.0-slim AS build
WORKDIR /src

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# "build" is the US site; "build:uk" is the UK section (baseHref /uk/, GBP),
# the same code built with angular.json's "uk" configuration.
ARG BUILD_SCRIPT=build
RUN npm run ${BUILD_SCRIPT}

FROM node:24.19.0-slim AS final
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /src/dist/frontend ./dist/frontend
COPY --from=build /src/node_modules ./node_modules

ENV PORT=4000
EXPOSE 4000

ENTRYPOINT ["node", "dist/frontend/server/server.mjs"]
