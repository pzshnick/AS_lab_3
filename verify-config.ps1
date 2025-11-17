# Configuration Verification Script

Write-Host "`n╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Configuration Verification                              ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# Check RabbitMQ
Write-Host "[1] Checking RabbitMQ..." -ForegroundColor Yellow
$rabbitMQ = docker ps --filter "name=rabbitmq" --format "{{.Names}}"
if ($rabbitMQ -eq "rabbitmq") {
    Write-Host "  ✓ RabbitMQ is running" -ForegroundColor Green
} else {
    Write-Host "  ✗ RabbitMQ is NOT running" -ForegroundColor Red
    Write-Host "    Run: docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 -e RABBITMQ_DEFAULT_USER=planora -e RABBITMQ_DEFAULT_PASS=planora rabbitmq:3.13-management" -ForegroundColor Yellow
}

# Check SharedModels.cs hostname
Write-Host "`n[2] Checking SharedModels.cs RabbitMQ settings..." -ForegroundColor Yellow
$sharedModels = Get-Content "Schedule_lab_3\SharedModels\SharedModels.cs" -Raw
if ($sharedModels -match 'HostName = "localhost"') {
    Write-Host "  ✓ HostName is set to 'localhost'" -ForegroundColor Green
} else {
    Write-Host "  ✗ HostName is NOT set to 'localhost'" -ForegroundColor Red
}
if ($sharedModels -match 'UserName = "planora"') {
    Write-Host "  ✓ UserName is set to 'planora'" -ForegroundColor Green
} else {
    Write-Host "  ✗ UserName is NOT set to 'planora'" -ForegroundColor Red
}

# Check NotificationService appsettings.json
Write-Host "`n[3] Checking NotificationService appsettings..." -ForegroundColor Yellow
$notifSettings = Get-Content "Schedule_lab_3\NotificationService\appsettings.json" -Raw | ConvertFrom-Json
if ($notifSettings.RabbitMQ.Host -eq "localhost") {
    Write-Host "  ✓ Host is set to 'localhost'" -ForegroundColor Green
} else {
    Write-Host "  ✗ Host is set to '$($notifSettings.RabbitMQ.Host)' instead of 'localhost'" -ForegroundColor Red
}

# Check ports in launchSettings
Write-Host "`n[4] Checking service ports..." -ForegroundColor Yellow
$services = @{
    "ScheduleService" = 5001
    "OptimizationService" = 5002
    "NotificationService" = 5003
    "AnalyticsService" = 5004
    "CatalogService" = 5005
}

foreach ($service in $services.GetEnumerator()) {
    $launchSettings = Get-Content "Schedule_lab_3\$($service.Key)\Properties\launchSettings.json" -Raw | ConvertFrom-Json
    $httpProfile = $launchSettings.profiles.http
    if ($httpProfile.applicationUrl -match ":$($service.Value)") {
        Write-Host "  ✓ $($service.Key) is configured for port $($service.Value)" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $($service.Key) is NOT configured for port $($service.Value)" -ForegroundColor Red
        Write-Host "    Current: $($httpProfile.applicationUrl)" -ForegroundColor Yellow
    }
}

# Check Vite config
Write-Host "`n[5] Checking Vite proxy configuration..." -ForegroundColor Yellow
$viteConfig = Get-Content "Client_lab_3\schedule-ui\vite.config.ts" -Raw
if ($viteConfig -match '/api/schedules.*localhost:5001' -and
    $viteConfig -match '/api/catalog.*localhost:5005' -and
    $viteConfig -match '/api/analytics.*localhost:5004') {
    Write-Host "  ✓ Vite proxy is properly configured" -ForegroundColor Green
} else {
    Write-Host "  ✗ Vite proxy may have issues" -ForegroundColor Red
}

Write-Host "`n╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Verification Complete                                   ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan
