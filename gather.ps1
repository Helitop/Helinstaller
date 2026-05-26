$ErrorActionPreference = "Stop"
Write-Host "=== STARTING PROJECT GATHERER ===" -ForegroundColor Yellow

# Searching for files, ignoring build garbage
$files = Get-ChildItem -Recurse -Include *.cs, *.xaml, *.json, *.csproj | 
    Where-Object { $_.FullName -notlike "*\obj\*" -and $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\.git\*" }

if ($files.Count -eq 0) {
    Write-Host "ERROR: No files found! Check your folder." -ForegroundColor Red
} else {
    $output = "full_project.txt"
    if (Test-Path $output) { Remove-Item $output }
    
    $i = 0
    foreach ($file in $files) {
        $i++
        # Simple progress output
        $msg = "[{0}/{1}] Adding: {2}" -f $i, $files.Count, $file.Name
        Write-Host $msg -ForegroundColor Cyan
        
        "=== FILE: $($file.FullName) ===" | Out-File -Append -FilePath $output -Encoding utf8
        Get-Content $file.FullName | Out-File -Append -FilePath $output -Encoding utf8
        "`n`n" | Out-File -Append -FilePath $output -Encoding utf8
    }
    Write-Host "`nDONE! Output file created: $output" -ForegroundColor Green
}

Write-Host "`nPress ENTER to exit..." -ForegroundColor Yellow
Read-Host