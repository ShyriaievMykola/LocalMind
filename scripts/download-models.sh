#!/usr/bin/env bash
# Завантажує embedding-модель у models/ і перевіряє SHA-256.
# Запуск: ./scripts/download-models.sh [модель|all]   (без параметра — multilingual-e5-small)
# Моделі: multilingual-e5-small multilingual-e5-base paraphrase-multilingual-MiniLM-L12-v2 paraphrase-multilingual-mpnet-base-v2
# Каталог моделей (хеші, версії) дублює LocalMind:ModelCatalog в appsettings.json; тест звіряє їх.
set -euo pipefail

all_models="multilingual-e5-small multilingual-e5-base paraphrase-multilingual-MiniLM-L12-v2 paraphrase-multilingual-mpnet-base-v2"
selected="${1:-multilingual-e5-small}"

sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    else
        shasum -a 256 "$1" | cut -d ' ' -f 1
    fi
}

select_model() {
    case "$1" in
        multilingual-e5-small)
            repository="intfloat/multilingual-e5-small"
            revision="614241f622f53c4eeff9890bdc4f31cfecc418b3"
            model_file="onnx/model.onnx"
            model_sha="ca456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665"
            tokenizer_file="sentencepiece.bpe.model"
            tokenizer_sha="cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"
            size_mb=448
            ;;
        multilingual-e5-base)
            repository="intfloat/multilingual-e5-base"
            revision="d128750597153bb5987e10b1c3493a34e5a4502a"
            model_file="onnx/model.onnx"
            model_sha="84a4d426f7e87a6bf5bf195f0bae2c4a7d15f675b23ca96f42fab8326d7a77aa"
            tokenizer_file="sentencepiece.bpe.model"
            tokenizer_sha="cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"
            size_mb=1058
            ;;
        paraphrase-multilingual-MiniLM-L12-v2)
            repository="sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
            revision="e8f8c211226b894fcb81acc59f3b34ba3efd5f42"
            model_file="onnx/model.onnx"
            model_sha="10f7a088420252b26caf819236ca2c9d2987afd0fc06fec7553b542a5655a05a"
            tokenizer_file="sentencepiece.bpe.model"
            tokenizer_sha="cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"
            size_mb=448
            ;;
        paraphrase-multilingual-mpnet-base-v2)
            repository="sentence-transformers/paraphrase-multilingual-mpnet-base-v2"
            revision="4328cf26390c98c5e3c738b4460a05b95f4911f5"
            model_file="onnx/model.onnx"
            model_sha="253e00bb467fcdcac714a7b2443330c28ffbecb6d1f791c92caaf2af468bfbaa"
            tokenizer_file="sentencepiece.bpe.model"
            tokenizer_sha="cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"
            size_mb=1058
            ;;
        *)
            echo "Невідома модель «$1». Доступні: $all_models або all." >&2
            exit 2
            ;;
    esac
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

fetch_model() {
    local model_name="$1"
    select_model "$model_name"
    base_url="https://huggingface.co/$repository/resolve/$revision"
    target_dir="$(cd "$(dirname "$0")/.." && pwd)/models/$model_name"
    echo "$model_name (~${size_mb} МБ)"
    mkdir -p "$target_dir"
    download "$tokenizer_file" "sentencepiece.bpe.model" "$tokenizer_sha"
    download "$model_file" "model.onnx" "$model_sha"
}

if [[ "$selected" == "all" ]]; then
    for model_name in $all_models; do
        fetch_model "$model_name"
    done
else
    fetch_model "$selected"
fi
