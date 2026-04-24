FROM node:20-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build:messaging

FROM node:20-alpine AS runner
RUN apk add --no-cache libc6-compat
WORKDIR /app
COPY package*.json ./
RUN npm ci --omit=dev
COPY --from=builder /app/dist ./dist
RUN chown -R node:node /app
USER node
ENV NODE_ENV=production
CMD ["node", "dist/apps/messaging/main.js"]
