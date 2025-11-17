# Schedule Management System - Startup Script

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Schedule Management System - Local Development          ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# Check if RabbitMQ is running
Write-Host "[1/6] Checking RabbitMQ..." -ForegroundColor Yellow
$rabbitMQ = docker ps --filter "name=rabbitmq" --format "{{.Names}}"
if ($rabbitMQ -ne "rabbitmq") {
    Write-Host "  ⚠ RabbitMQ not running. Starting..." -ForegroundColor Yellow
    docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 `
        -e RABBITMQ_DEFAULT_USER=planora `
        -e RABBITMQ_DEFAULT_PASS=planora `
        rabbitmq:3.13-management
    Write-Host "  ✓ RabbitMQ started. Waiting 15 seconds..." -ForegroundColor Green
    Start-Sleep -Seconds 15
} else {
    Write-Host "  ✓ RabbitMQ is already running" -ForegroundColor Green
}

# Start backend services in separate windows
$services = @(
    @{Name="ScheduleService"; Port=5001; Path="Schedule_lab_3\ScheduleService"},
    @{Name="OptimizationService"; Port=5002; Path="Schedule_lab_3\OptimizationService"},
    @{Name="NotificationService"; Port=5003; Path="Schedule_lab_3\NotificationService"},
    @{Name="AnalyticsService"; Port=5004; Path="Schedule_lab_3\AnalyticsService"},
    @{Name="CatalogService"; Port=5005; Path="Schedule_lab_3\CatalogService"}
)

$counter = 2
foreach ($svc in $services) {
    Write-Host "[$counter/6] Starting $($svc.Name) on port $($svc.Port)..." -ForegroundColor Yellow
    $fullPath = Join-Path $PSScriptRoot $svc.Path
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$fullPath'; Write-Host '═══ $($svc.Name) ═══' -ForegroundColor Cyan; dotnet run"
    $counter++
    Start-Sleep -Seconds 2
}

Write-Host "`n[6/6] Starting Frontend..." -ForegroundColor Yellow
$frontendPath = Join-Path $PSScriptRoot "Client_lab_3\schedule-ui"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$frontendPath'; Write-Host '═══ React Frontend ═══' -ForegroundColor Cyan; npm run dev"

Write-Host "`n✓ All services started!" -ForegroundColor Green
Write-Host "`nServices:" -ForegroundColor Cyan
Write-Host "  • RabbitMQ Management: http://localhost:15672 (planora/planora)" -ForegroundColor White
Write-Host "  • ScheduleService API: http://localhost:5001/swagger" -ForegroundColor White
Write-Host "  • OptimizationService API: http://localhost:5002/swagger" -ForegroundColor White
Write-Host "  • AnalyticsService API: http://localhost:5004/swagger" -ForegroundColor White
Write-Host "  • CatalogService API: http://localhost:5005/swagger" -ForegroundColor White
Write-Host "  • Frontend UI: http://localhost:5173" -ForegroundColor Yellow
Write-Host "`nPress any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
