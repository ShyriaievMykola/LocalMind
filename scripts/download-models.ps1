# Завантажує embedding-модель у models/ і перевіряє SHA-256.
# Запуск: .\scripts\download-models.ps1 [-Model <назва>|all]   (без параметра — multilingual-e5-small)
# Каталог моделей (хеші, версії) дублює LocalMind:ModelCatalog в appsettings.json; тест звіряє їх.
param(
    [string] $Model = 'multilingual-e5-small'
)

$ErrorActionPreference = 'Stop'

$catalog = [ordered]@{
    'multilingual-e5-small' = @{
        Repository = 'intfloat/multilingual-e5-small'
        Revision = '614241f622f53c4eeff9890bdc4f31cfecc418b3'
        ModelFile = 'onnx/model.onnx'
        ModelSha256 = 'ca456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665'
        TokenizerFile = 'sentencepiece.bpe.model'
        TokenizerSha256 = 'cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865'
        SizeMb = 448
    }
    'multilingual-e5-base' = @{
        Repository = 'intfloat/multilingual-e5-base'
        Revision = 'd128750597153bb5987e10b1c3493a34e5a4502a'
        ModelFile = 'onnx/model.onnx'
        ModelSha256 = '84a4d426f7e87a6bf5bf195f0bae2c4a7d15f675b23ca96f42fab8326d7a77aa'
        TokenizerFile = 'sentencepiece.bpe.model'
        TokenizerSha256 = 'cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865'
        SizeMb = 1058
    }
    'paraphrase-multilingual-MiniLM-L12-v2' = @{
        Repository = 'sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2'
        Revision = 'e8f8c211226b894fcb81acc59f3b34ba3efd5f42'
        ModelFile = 'onnx/model.onnx'
        ModelSha256 = '10f7a088420252b26caf819236ca2c9d2987afd0fc06fec7553b542a5655a05a'
        TokenizerFile = 'sentencepiece.bpe.model'
        TokenizerSha256 = 'cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865'
        SizeMb = 448
    }
    'paraphrase-multilingual-mpnet-base-v2' = @{
        Repository = 'sentence-transformers/paraphrase-multilingual-mpnet-base-v2'
        Revision = '4328cf26390c98c5e3c738b4460a05b95f4911f5'
        ModelFile = 'onnx/model.onnx'
        ModelSha256 = '253e00bb467fcdcac714a7b2443330c28ffbecb6d1f791c92caaf2af468bfbaa'
        TokenizerFile = 'sentencepiece.bpe.model'
        TokenizerSha256 = 'cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865'
        SizeMb = 1058
    }
}

function Get-Sha256([string] $Path) {
    (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Save-ModelFile([string] $TargetDir, [string] $BaseUrl, [string] $RemotePath, [string] $FileName, [string] $ExpectedHash) {
    $target = Join-Path $TargetDir $FileName

    if ((Test-Path $target) -and (Get-Sha256 $target) -eq $ExpectedHash) {
        Write-Host "${FileName}: вже є, хеш збігається"
        return
    }

    Write-Host "${FileName}: завантаження..."
    $temp = "$target.tmp"
    # Відносний шлях: curl.exe не відкриває файли за шляхом із кирилицею.
    Push-Location $TargetDir
    try {
        & curl.exe -fL --retry 3 --output "$FileName.tmp" "$BaseUrl/$RemotePath"
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

function Save-Model([string] $Name) {
    $entry = $catalog[$Name]
    $baseUrl = "https://huggingface.co/$($entry.Repository)/resolve/$($entry.Revision)"
    $targetDir = Join-Path (Split-Path $PSScriptRoot -Parent) "models\$Name"
    Write-Host "$Name (~$($entry.SizeMb) МБ)"
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Save-ModelFile $targetDir $baseUrl $entry.TokenizerFile 'sentencepiece.bpe.model' $entry.TokenizerSha256
    Save-ModelFile $targetDir $baseUrl $entry.ModelFile 'model.onnx' $entry.ModelSha256
}

if ($Model -eq 'all') {
    foreach ($name in $catalog.Keys) { Save-Model $name }
}
elseif ($catalog.Contains($Model)) {
    Save-Model $Model
}
else {
    Write-Error "Невідома модель «$Model». Доступні: $($catalog.Keys -join ', ') або all."
    exit 2
}
