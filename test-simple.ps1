# Simple test for schedule creation

Write-Host "Creating a simple schedule..." -ForegroundColor Yellow

$schedule = @{
    name = "Simple Test"
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

Write-Host "Sending request..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5001/api/schedules" -Method Post -Body $schedule -ContentType "application/json" -UseBasicParsing
    Write-Host "Success! Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Green
    $response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
} catch {
    Write-Host "Failed! Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
    Write-Host "Error:" -ForegroundColor Red
    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
    $reader.BaseStream.Position = 0
    $responseBody = $reader.ReadToEnd()
    Write-Host $responseBody
}
