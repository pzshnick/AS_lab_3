# Test schedule with conflicts

Write-Host "Creating a schedule WITH conflicts (same room, overlapping time)..." -ForegroundColor Yellow

$schedule = @{
    name = "Conflict Test"
    entries = @(
        @{
            subject = "Math"
            teacher = "Prof. Smith"
            group = "CS-101"
            room = "Room 101"
            dayOfWeek = 1
            startTime = "09:00:00"
            endTime = "10:30:00"
        },
        @{
            subject = "Physics"
            teacher = "Prof. Johnson"
            group = "CS-102"
            room = "Room 101"      # Same room!
            dayOfWeek = 1           # Same day!
            startTime = "09:30:00"  # Overlapping time!
            endTime = "11:00:00"
        }
    )
} | ConvertTo-Json -Depth 10

Write-Host "Sending request..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5001/api/schedules" -Method Post -Body $schedule -ContentType "application/json" -UseBasicParsing
    Write-Host "UNEXPECTED: Schedule was accepted (should have been rejected)" -ForegroundColor Red
    Write-Host "Response:" -ForegroundColor Yellow
    $response.Content
} catch {
    Write-Host "SUCCESS: Schedule rejected! Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Green
    Write-Host "Error details:" -ForegroundColor Green
    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
    $reader.BaseStream.Position = 0
    $responseBody = $reader.ReadToEnd()
    $json = $responseBody | ConvertFrom-Json
    Write-Host "Error: $($json.error)" -ForegroundColor Yellow
    Write-Host "Conflicts:" -ForegroundColor Yellow
    $json.conflicts | ForEach-Object { Write-Host "  - $_" -ForegroundColor Cyan }
}

Write-Host "`nChecking analytics..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

try {
    $stats = Invoke-RestMethod -Uri "http://localhost:5004/api/analytics/stats" -Method Get
    Write-Host "`nAnalytics Statistics:" -ForegroundColor Green
    Write-Host "  Total Schedules: $($stats.totalSchedules)" -ForegroundColor $(if ($stats.totalSchedules -gt 0) { "Green" } else { "Red" })
    Write-Host "  Total Updates: $($stats.totalUpdates)" -ForegroundColor $(if ($stats.totalUpdates -gt 0) { "Green" } else { "Red" })
    Write-Host "  Total Optimizations: $($stats.totalOptimizations)" -ForegroundColor White
    Write-Host "  Total Conflicts Detected: $($stats.totalConflictsDetected)" -ForegroundColor White
} catch {
    Write-Host "Failed to get analytics: $($_.Exception.Message)" -ForegroundColor Red
}
