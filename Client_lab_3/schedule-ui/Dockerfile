# 1) Build stage (alpine + реінсталяція без старого lock)
FROM node:22-alpine AS builder
WORKDIR /app

# Копіюємо маніфести
COPY package*.json ./

# ВАЖЛИВО: прибираємо lock, щоб npm розв'язав платформи під linux-musl
RUN rm -f package-lock.json
RUN npm install --no-audit --no-fund

# Копіюємо решту коду і будуємо
COPY . .
RUN npm run build

# 2) Runtime: nginx
FROM nginx:1.28-alpine
WORKDIR /usr/share/nginx/html

COPY --from=builder /app/dist ./
COPY ./nginx.conf /etc/nginx/nginx.conf

EXPOSE 80
