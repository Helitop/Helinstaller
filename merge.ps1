# Имя итогового файла
$outputFile = "complete_project_code.txt"

# Очищаем старый файл, если он был
if (Test-Path $outputFile) { Remove-Item $outputFile }

# Папки-исключения
$excludeFolders = @("bin", "obj", ".vs", "Properties", "packages", ".git")

Write-Host "Сканирую директорию..." -ForegroundColor Cyan

# Получаем файлы .cs и .xaml
$files = Get-ChildItem -Recurse -Include *.cs, *.xaml, *.json | Where-Object {
    $path = $_.FullName
    
    # Пропускаем сам выходной файл, если он вдруг попал в выборку
    if ($_.Name -eq $outputFile) { return $false }
    
    # Проверяем папки-исключения
    foreach ($folder in $excludeFolders) {
        if ($path -like "*\$folder\*") { return $false }
    }
    return $true
}

# Считаем типы для красоты
$csCount = ($files | Where-Object { $_.Extension -eq ".cs" }).Count
$xamlCount = ($files | Where-Object { $_.Extension -eq ".xaml" }).Count

Write-Host "Найдено файлов для сборки: $($files.Count) (C#: $csCount, XAML: $xamlCount)" -ForegroundColor Cyan

# 1. ГЕНЕРИРУЕМ ДЕРЕВО ПРОЕКТА
$treeHeader = "========================================`r`nСТРУКТУРА ПРОЕКТА`r`n========================================`r`n"
Out-File -FilePath $outputFile -InputObject $treeHeader -Append -Encoding utf8

# Берем относительные пути, сортируем и превращаем в подобие дерева
$projectRoot = (Get-Location).Path
$relativePaths = $files | ForEach-Object { $_.FullName.Replace($projectRoot, ".").Replace("\", "/") } | Sort-Object

foreach ($path in $relativePaths) {
    # Разбиваем путь, чтобы сделать красивые отступы
    $segments = $path.Split('/')
    $indent = "    " * ($segments.Count - 1)
    $treeLine = "$indent- $($segments[-1])"
    Out-File -FilePath $outputFile -InputObject $treeLine -Append -Encoding utf8
}

Out-File -FilePath $outputFile -InputObject "`r`n" -Append -Encoding utf8


# 2. СОБИРАЕМ КОНТЕНТ ФАЙЛОВ
foreach ($file in $files) {
    $relativePath = $file.FullName.Replace($projectRoot, ".")
    
    # Заголовок для конкретного файла (с указанием типа, чтоб ИИ лучше ориентировался)
    $fileType = $file.Extension.ToUpper().Replace(".","")
    $header = "========================================`r`nFILE [$fileType]: $relativePath`r`n========================================`r`n"
    
    Out-File -FilePath $outputFile -InputObject $header -Append -Encoding utf8
    
    # Читаем и пишем контент
    $content = Get-Content -Path $file.FullName -Raw
    Out-File -FilePath $outputFile -InputObject $content -Append -Encoding utf8
    
    # Добавляем разделитель в конце файла
    Out-File -FilePath $outputFile -InputObject "`r`n" -Append -Encoding utf8
}

Write-Host "Готово! Дерево и код упакованы в $outputFile" -ForegroundColor Green