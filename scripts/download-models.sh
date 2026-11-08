#!/usr/bin/env bash
# Завантажує embedding-модель у models/ і перевіряє SHA-256. Запуск: ./scripts/download-models.sh
set -euo pipefail

model_name="multilingual-e5-small"
base_url="https://huggingface.co/intfloat/multilingual-e5-small/resolve/614241f622f53c4eeff9890bdc4f31cfecc418b3"
target_dir="$(cd "$(dirname "$0")/.." && pwd)/models/$model_name"

sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    else
        shasum -a 256 "$1" | cut -d ' ' -f 1
    fi
}

# Аргументи: шлях на Hugging Face, локальна назва, очікуваний SHA-256.
download() {
    local target="$target_dir/$2"

    if [[ -f "$target" && "$(sha256 "$target")" == "$3" ]]; then
        echo "$2: вже є, хеш збігається"
        return
    fi

    echo "$2: завантаження..."
    # Відносний шлях: curl для Windows не відкриває файли за шляхом із кирилицею.
    (cd "$target_dir" && curl -fL --retry 3 --output "$2.tmp" "$base_url/$1")

    local actual
    actual="$(sha256 "$target.tmp")"
    if [[ "$actual" != "$3" ]]; then
        rm -f "$target.tmp"
        echo "$2: хеш не збігається (очікувався $3, отримано $actual)" >&2
        exit 1
    fi

    mv -f "$target.tmp" "$target"
    echo "$2: готово"
}

mkdir -p "$target_dir"
download "sentencepiece.bpe.model" "sentencepiece.bpe.model" "cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"
download "onnx/model.onnx" "model.onnx" "ca456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665"
