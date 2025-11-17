# === Build stage ===
FROM node:20-bookworm AS build
WORKDIR /app

# Копіюємо тільки маніфести для кращого кешування
COPY package*.json ./

# Спочатку пробуємо reproducible інсталяцію,
# якщо спрацьовує npm-баґ з optional deps — робимо fallback без lockfile
RUN npm ci || (rm -rf node_modules package-lock.json && npm install)

# Далі код
COPY . .

# Явно ставимо нативний бінар Rollup під поточну платформу
# (BuildKit передає TARGETPLATFORM: linux/amd64 або linux/arm64)
ARG TARGETPLATFORM
RUN set -eux; \
    case "$TARGETPLATFORM" in \
      "linux/amd64") npm i -D @rollup/rollup-linux-x64-gnu ;; \
      "linux/arm64") npm i -D @rollup/rollup-linux-arm64-gnu ;; \
      *) echo "Unknown platform: $TARGETPLATFORM; trying x64-gnu"; npm i -D @rollup/rollup-linux-x64-gnu || true ;; \
    esac

# Білд фронтенду
RUN npm run build

# === Production stage ===
FROM nginx:alpine AS production
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx","-g","daemon off;"]
