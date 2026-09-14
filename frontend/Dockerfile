# syntax=docker/dockerfile:1

# FleetStream dashboard (Next.js 16, App Router).
# Multi-stage: full dev dependencies for the build, then a minimal standalone
# server runtime. `output: "standalone"` in next.config.ts makes the runner
# stage possible without node_modules in the final image.

# ---- deps / build ---------------------------------------------------------
FROM node:24-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
# `ci` (not `install`) so the lockfile is authoritative in every image build.
RUN npm ci

FROM node:24-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# NEXT_PUBLIC_* values are inlined at build time, so they must be supplied as
# build args (not just runtime env) for a staging/prod image to reach the right
# BFF and for the CSP connect-src to name it.
ARG NEXT_PUBLIC_API_BASE_URL=http://localhost:8080
ARG NEXT_PUBLIC_SIGNALR_HUB_URL=http://localhost:8080/hubs/v1/fleet
ARG CSP_CONNECT_SRC=
ENV NEXT_PUBLIC_API_BASE_URL=$NEXT_PUBLIC_API_BASE_URL
ENV NEXT_PUBLIC_SIGNALR_HUB_URL=$NEXT_PUBLIC_SIGNALR_HUB_URL
ENV CSP_CONNECT_SRC=$CSP_CONNECT_SRC
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

# ---- runner ---------------------------------------------------------------
FROM node:24-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

# Next's standalone bundle traces only the files the server actually needs.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public

# Never run as root in the cluster.
USER node
EXPOSE 3000

# The dashboard talks to the BFF only through the browser, so no secrets are
# required at runtime; a failing start is always a real fault.
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD wget -qO- "http://127.0.0.1:${PORT}/login" >/dev/null 2>&1 || exit 1

CMD ["node", "server.js"]
