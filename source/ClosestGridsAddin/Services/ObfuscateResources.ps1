# Script para ofuscar nombres de recursos embebidos
# Ejecutar ANTES de compilar

param(
    [string]$ProjectRoot = "D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo\source\ClosestGridsAddin"
)

Write-Host "🔒 Ofuscando recursos embebidos..." -ForegroundColor Cyan

# Función para generar nombre aleatorio
function Get-RandomName {
    param([int]$Length = 20)
    $chars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
    $name = -join ((1..$Length) | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
    return $name
}

# Crear mapeo de nombres originales → ofuscados
$mapping = @{}
$mappingFile = Join-Path $ProjectRoot "Services\ResourceMapping.txt"

# Buscar todos los recursos
$resources = Get-ChildItem -Path $ProjectRoot -Recurse -Include *.png,*.jpg,*.xaml,*.xml,*.ico -Exclude bin,obj

Write-Host "📦 Encontrados $($resources.Count) recursos" -ForegroundColor Yellow

foreach ($resource in $resources) {
    $originalName = $resource.Name
    $extension = $resource.Extension
    $newName = "$(Get-RandomName)$extension"

    # Guardar mapeo
    $relativePath = $resource.FullName.Replace($ProjectRoot, "")
    $mapping[$relativePath] = $newName

    # Renombrar archivo
    $newPath = Join-Path $resource.DirectoryName $newName
    Rename-Item -Path $resource.FullName -NewName $newName -Force

    Write-Host "  ✅ $originalName → $newName" -ForegroundColor Green
}

# Guardar mapeo para referencia futura
$mapping.GetEnumerator() | ForEach-Object {
    "$($_.Key) → $($_.Value)"
} | Out-File -FilePath $mappingFile -Encoding UTF8

Write-Host "`n✅ Ofuscación completa!" -ForegroundColor Green
Write-Host "📝 Mapeo guardado en: $mappingFile" -ForegroundColor Cyan
Write-Host "⚠️  IMPORTANTE: Actualizar referencias en .csproj y código!" -ForegroundColor Yellow
