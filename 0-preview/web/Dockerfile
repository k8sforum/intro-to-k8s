FROM node:22-alpine AS build

WORKDIR /app/web

ARG VITE_API_BASE_URL
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/. .
RUN npm run build

FROM nginx:1.27-alpine AS runtime

COPY --from=build /app/web/dist /usr/share/nginx/html
COPY web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
