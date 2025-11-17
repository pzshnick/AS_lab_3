# Schedule Management System - Setup Guide

## Запуск без Docker (Local Development)

### Автоматичний запуск

Запустіть PowerShell скрипт:

```powershell
.\start-all.ps1
```

Цей скрипт:
1. Перевірить і запустить RabbitMQ (якщо потрібно)
2. Запустить всі 5 backend сервісів
3. Запустить React frontend

### Ручний запуск

#### 1. Запуск RabbitMQ

```powershell
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 `
  -e RABBITMQ_DEFAULT_USER=planora `
  -e RABBITMQ_DEFAULT_PASS=planora `
  rabbitmq:3.13-management
```

Зачекайте ~15 секунд для повного запуску.

#### 2. Запуск Backend Сервісів

Відкрийте 5 окремих терміналів PowerShell:

**Термінал 1 - ScheduleService (порт 5001):**
```powershell
cd Schedule_lab_3\ScheduleService
dotnet run
```

**Термінал 2 - OptimizationService (порт 5002):**
```powershell
cd Schedule_lab_3\OptimizationService
dotnet run
```

**Термінал 3 - NotificationService (порт 5003):**
```powershell
cd Schedule_lab_3\NotificationService
dotnet run
```

**Термінал 4 - AnalyticsService (порт 5004):**
```powershell
cd Schedule_lab_3\AnalyticsService
dotnet run
```

**Термінал 5 - CatalogService (порт 5005):**
```powershell
cd Schedule_lab_3\CatalogService
dotnet run
```

#### 3. Запуск Frontend

**Термінал 6 - React Frontend:**
```powershell
cd Client_lab_3\schedule-ui
npm run dev
```

### Доступ до сервісів

- **Frontend UI:** http://localhost:5173
- **RabbitMQ Management:** http://localhost:15672 (логін: `planora`, пароль: `planora`)
- **ScheduleService Swagger:** http://localhost:5001/swagger
- **OptimizationService Swagger:** http://localhost:5002/swagger
- **AnalyticsService Swagger:** http://localhost:5004/swagger
- **CatalogService Swagger:** http://localhost:5005/swagger

### Архітектура

```
┌─────────────────┐
│  React Frontend │ :5173
│   (Vite + TS)   │
└────────┬────────┘
         │ HTTP
    ┌────┴────────────────────────────┐
    │         API Gateway             │
    │      (Vite Proxy)               │
    └─┬───┬────┬────┬────┬───────────┘
      │   │    │    │    │
   ┌──┴┐┌─┴─┐┌─┴─┐┌─┴─┐┌─┴──┐
   │Sch││Opt││Cat││Ana││Not │  Services
   │edu││imi││alo││lyt││ifi │
   │le ││zat││g  ││ics││cat │
   │:50││ion││:50││:50││ion │
   │01 ││:50││05 ││04 ││:50 │
   └─┬─┘└─┬─┘└─┬─┘└─┬─┘└─┬──┘  03
     │    │    │    │    │
     └────┴────┴────┴────┴─────┐
                    ┌───────────┴────┐
                    │   RabbitMQ     │
                    │     :5672      │
                    └────────────────┘
```

### Перевірка роботи

1. Відкрийте Frontend: http://localhost:5173
2. Створіть новий розклад
3. Перевірте Analytics вкладку - мають оновитися лічильники
4. Натисніть "Optimize" на розкладі
5. Перевірте NotificationService консоль - мають з'явитися повідомлення

### Troubleshooting

**Проблема:** Services не можуть підключитися до RabbitMQ

**Рішення:**
```powershell
# Перевірте чи працює RabbitMQ
docker ps | findstr rabbitmq

# Перезапустіть RabbitMQ
docker stop rabbitmq
docker rm rabbitmq
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 `
  -e RABBITMQ_DEFAULT_USER=planora `
  -e RABBITMQ_DEFAULT_PASS=planora `
  rabbitmq:3.13-management
```

**Проблема:** Порт вже використовується

**Рішення:**
```powershell
# Знайдіть процес на порту (наприклад 5001)
netstat -ano | findstr :5001

# Вбийте процес (замініть PID на ID з попередньої команди)
taskkill /PID <PID> /F
```

**Проблема:** Frontend не бачить API

**Рішення:**
- Переконайтеся, що всі сервіси запущені
- Перевірте Vite proxy в `Client_lab_3/schedule-ui/vite.config.ts`
- Перезапустіть frontend

### Зупинка сервісів

1. Натисніть Ctrl+C в кожному терміналі сервісу
2. Зупиніть RabbitMQ:
```powershell
docker stop rabbitmq
```

### Cleanup

Видаліть RabbitMQ контейнер:
```powershell
docker stop rabbitmq
docker rm rabbitmq
```

## Налаштування для Docker (Production)

Для запуску всіх сервісів через Docker Compose:

```powershell
docker-compose up --build
```

**Примітка:** Для Docker потрібно змінити `RabbitMqSettings.HostName` в `SharedModels/SharedModels.cs` з `"localhost"` на `"rabbitmq"`.
