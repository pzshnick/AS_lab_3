# Test script for schedule validation and analytics fixes

Write-Host "`n=== Testing Schedule Validation ===" -ForegroundColor Cyan

# Test 1: Create a schedule WITHOUT conflicts (should succeed)
Write-Host "`nTest 1: Creating schedule without conflicts..." -ForegroundColor Yellow
$scheduleNoConflict = @{
    name = "Test Schedule - No Conflicts"
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
            room = "Room 102"
            dayOfWeek = 1
            startTime = "11:00:00"
            endTime = "12:30:00"
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $response1 = Invoke-RestMethod -Uri "http://localhost:5001/api/schedules" -Method Post -Body $scheduleNoConflict -ContentType "application/json"
    Write-Host "SUCCESS: Schedule created without conflicts" -ForegroundColor Green
    Write-Host "Schedule ID: $($response1.id)" -ForegroundColor Green
    $createdScheduleId = $response1.id
} catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.Response
}

Start-Sleep -Seconds 2

# Test 2: Try to create a schedule WITH conflicts (should fail)
Write-Host "`nTest 2: Creating schedule with conflicts (same room at same time)..." -ForegroundColor Yellow
$scheduleWithConflict = @{
    name = "Test Schedule - With Conflicts"
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
            room = "Room 101"  # Same room!
            dayOfWeek = 1       # Same day!
            startTime = "09:30:00"  # Overlapping time!
            endTime = "11:00:00"
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $response2 = Invoke-RestMethod -Uri "http://localhost:5001/api/schedules" -Method Post -Body $scheduleWithConflict -ContentType "application/json"
    Write-Host "FAILED: Schedule with conflicts was accepted (should have been rejected)" -ForegroundColor Red
} catch {
    $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
    Write-Host "SUCCESS: Schedule rejected due to conflicts" -ForegroundColor Green
    Write-Host "Error: $($errorDetails.error)" -ForegroundColor Green
    Write-Host "Conflicts detected:" -ForegroundColor Green
    $errorDetails.conflicts | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}

Start-Sleep -Seconds 2

# Test 3: Check Analytics
Write-Host "`n=== Testing Analytics ===" -ForegroundColor Cyan
Write-Host "`nTest 3: Checking analytics statistics..." -ForegroundColor Yellow

Start-Sleep -Seconds 3  # Wait for events to be processed

try {
    $stats = Invoke-RestMethod -Uri "http://localhost:5004/api/analytics/stats" -Method Get
    Write-Host "Analytics Statistics:" -ForegroundColor Green
    Write-Host "  Total Schedules: $($stats.totalSchedules)" -ForegroundColor $(if ($stats.totalSchedules -gt 0) { "Green" } else { "Red" })
    Write-Host "  Total Updates: $($stats.totalUpdates)" -ForegroundColor $(if ($stats.totalUpdates -gt 0) { "Green" } else { "Red" })
    Write-Host "  Total Optimizations: $($stats.totalOptimizations)" -ForegroundColor White
    Write-Host "  Total Conflicts Detected: $($stats.totalConflictsDetected)" -ForegroundColor White

    if ($stats.totalSchedules -gt 0 -and $stats.totalUpdates -gt 0) {
        Write-Host "`nSUCCESS: Analytics is showing real data!" -ForegroundColor Green
    } else {
        Write-Host "`nWARNING: Analytics might still be showing zeros" -ForegroundColor Yellow
    }
} catch {
    Write-Host "FAILED: Could not retrieve analytics: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: Get recent events
Write-Host "`nTest 4: Checking recent analytics events..." -ForegroundColor Yellow
try {
    $events = Invoke-RestMethod -Uri "http://localhost:5004/api/analytics/events" -Method Get
    Write-Host "Recent Events Count: $($events.Count)" -ForegroundColor Green
    if ($events.Count -gt 0) {
        Write-Host "Recent events:" -ForegroundColor Green
        $events | Select-Object -First 5 | ForEach-Object {
            Write-Host "  - Type: $($_.type), Time: $($_.timestamp)" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "FAILED: Could not retrieve events: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== Test Summary ===" -ForegroundColor Cyan
Write-Host "1. Schedule validation: Tests completed" -ForegroundColor White
Write-Host "2. Analytics tracking: Tests completed" -ForegroundColor White
Write-Host "`nPlease review the results above to verify both fixes are working correctly." -ForegroundColor White
