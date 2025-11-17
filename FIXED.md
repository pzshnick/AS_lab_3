# Виправлені проблеми

## Що було виправлено:

### 1. ✅ RabbitMQ Credentials в AnalyticsService
**Проблема:** AnalyticsService не використовував UserName та Password для підключення до RabbitMQ

**Виправлено:** `Schedule_lab_3/AnalyticsService/Program.cs:203`
```csharp
var factory = new ConnectionFactory
{
    HostName = RabbitMqSettings.HostName,
    UserName = RabbitMqSettings.UserName,  // ДОДАНО
    Password = RabbitMqSettings.Password   // ДОДАНО
};
```

### 2. ✅ RabbitMQ Hostname
**Виправлено:** `Schedule_lab_3/SharedModels/SharedModels.cs:165`
- Змінено з `"rabbitmq"` на `"localhost"`

### 3. ✅ NotificationService appsettings.json
**Виправлено:** `Schedule_lab_3/NotificationService/appsettings.json`
- Host: `"localhost"` замість `"rabbitmq"`

### 4. ✅ Порти сервісів
Всі сервіси налаштовані на правильні порти:
- ScheduleService: 5001
- OptimizationService: 5002
- NotificationService: 5003
- AnalyticsService: 5004
- CatalogService: 5005

### 5. ✅ Vite Proxy
Налаштовано окремі proxy для кожного API

### 6. ✅ ScheduleService виправлення
- Додано `AddCors()` та `AddHttpClient()` ПЕРЕД `builder.Build()`
- Виправлено URL OptimizationService на `http://localhost:5002`

### 7. ✅ Валідація форми
Додано перевірки:
- Всі поля мають бути заповнені
- Час закінчення не може бути раніше початку

### 8. ✅ Аналітика
Виправлено підрахунок розкладів через `HashSet<int>` замість `Dictionary.Count`

---

## Як запустити (ПІСЛЯ виправлень):

### Варіант 1: Автоматично
```powershell
.\start-all.ps1
```

### Варіант 2: Вручну

#### 1. RabbitMQ
```powershell
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 `
  -e RABBITMQ_DEFAULT_USER=planora `
  -e RABBITMQ_DEFAULT_PASS=planora `
  rabbitmq:3.13-management
```
Зачекайте 15 секунд.

#### 2. Backend (5 термі

налів)
```powershell
# Термінал 1
cd Schedule_lab_3\ScheduleService
dotnet run

# Термінал 2
cd Schedule_lab_3\OptimizationService
dotnet run

# Термінал 3
cd Schedule_lab_3\NotificationService
dotnet run

# Термінал 4
cd Schedule_lab_3\AnalyticsService
dotnet run

# Термінал 5
cd Schedule_lab_3\CatalogService
dotnet run
```

#### 3. Frontend
```powershell
cd Client_lab_3\schedule-ui
npm run dev
```

---

## Перевірка конфігурації
```powershell
.\verify-config.ps1
```

---

## Доступ до сервісів
- **Frontend:** http://localhost:5173
- **RabbitMQ Management:** http://localhost:15672 (planora/planora)
- **Swagger APIs:**
  - ScheduleService: http://localhost:5001/swagger
  - OptimizationService: http://localhost:5002/swagger
  - AnalyticsService: http://localhost:5004/swagger
  - CatalogService: http://localhost:5005/swagger

---

## Troubleshooting

### AnalyticsService падає з "None of the specified endpoints were reachable"
**Рішення:** Переконайтеся що:
1. RabbitMQ запущений і доступний на localhost:5672
2. Проект перебудований після змін: `cd Schedule_lab_3\AnalyticsService && dotnet build`
3. Credentials правильні (planora/planora)

### Frontend показує 500 помилку
**Рішення:**
1. Перевірте чи запущений ScheduleService (http://localhost:5001/swagger)
2. Перевірте консоль ScheduleService на помилки RabbitMQ
3. Перезапустіть ScheduleService

### NotificationService не підключається
**Рішення:**
1. Перевірте appsettings.json - Host має бути "localhost"
2. Перезапустіть NotificationService

---

## Важливо!

Після будь-яких змін в `SharedModels/SharedModels.cs` потрібно:
```powershell
cd Schedule_lab_3
dotnet clean
dotnet build
```

Це перекомпілює всі проекти з новими налаштуваннями.
