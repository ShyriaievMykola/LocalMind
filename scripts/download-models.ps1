# Завантажує embedding-модель у models/ і перевіряє SHA-256. Запуск: .\scripts\download-models.ps1
$ErrorActionPreference = 'Stop'

$modelName = 'multilingual-e5-small'
$baseUrl = 'https://huggingface.co/intfloat/multilingual-e5-small/resolve/614241f622f53c4eeff9890bdc4f31cfecc418b3'
$targetDir = Join-Path (Split-Path $PSScriptRoot -Parent) "models\$modelName"

function Get-Sha256([string] $Path) {
    (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Save-ModelFile([string] $RemotePath, [string] $FileName, [string] $ExpectedHash) {
    $target = Join-Path $targetDir $FileName

    if ((Test-Path $target) -and (Get-Sha256 $target) -eq $ExpectedHash) {
        Write-Host "${FileName}: вже є, хеш збігається"
        return
    }

    Write-Host "${FileName}: завантаження..."
    $temp = "$target.tmp"
    # Відносний шлях: curl.exe не відкриває файли за шляхом із кирилицею.
    Push-Location $targetDir
    try {
        & curl.exe -fL --retry 3 --output "$FileName.tmp" "$baseUrl/$RemotePath"
    }
    finally {
        Pop-Location
    }

    if ($LASTEXITCODE -ne 0) {
        throw "${FileName}: не вдалося завантажити (curl: $LASTEXITCODE)"
    }

    $actual = Get-Sha256 $temp
    if ($actual -ne $ExpectedHash) {
        Remove-Item $temp -Force
        throw "${FileName}: хеш не збігається (очікувався $ExpectedHash, отримано $actual)"
    }

    Move-Item $temp $target -Force
    Write-Host "${FileName}: готово"
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Save-ModelFile 'sentencepiece.bpe.model' 'sentencepiece.bpe.model' 'cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865'
Save-ModelFile 'onnx/model.onnx' 'model.onnx' 'ca456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665'
