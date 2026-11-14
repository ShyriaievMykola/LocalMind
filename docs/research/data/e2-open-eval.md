# Оцінка пошуку: open-eval

## multilingual-e5-small, fixedSize, фрагмент 120 токенів, перекриття 15%

- Документів: 902, фрагментів: 27031
- Індексація: 614.8 с
- Розмір бази: 86.90 МБ
- Пікова пам'ять: 1495 МБ

## multilingual-e5-base, fixedSize, фрагмент 120 токенів, перекриття 15%

- Документів: 902, фрагментів: 27031
- Індексація: 2311.2 с
- Розмір бази: 139.49 МБ
- Пікова пам'ять: 3162 МБ

## paraphrase-multilingual-MiniLM-L12-v2, fixedSize, фрагмент 120 токенів, перекриття 15%

- Документів: 902, фрагментів: 26999
- Індексація: 933.3 с
- Розмір бази: 86.75 МБ
- Пікова пам'ять: 3162 МБ

## paraphrase-multilingual-mpnet-base-v2, fixedSize, фрагмент 120 токенів, перекриття 15%

- Документів: 902, фрагментів: 26999
- Індексація: 2782.7 с
- Розмір бази: 139.83 МБ
- Пікова пам'ять: 3162 МБ

| Прогін | Модель | Нарізка | Режим | Тип запитів | Запитів | k | Recall@k | MRR | nDCG@k | p50, мс | p95, мс |
|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | keyword | all | 90 | 10 | 0.488 | 0.705 | 0.492 | 8.6 | 41.3 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | keyword | exact | 30 | 10 | 0.894 | 1.000 | 0.924 | 4.8 | 62.2 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | keyword | paraphrase | 30 | 10 | 0.217 | 0.133 | 0.124 | 9.0 | 62.0 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | keyword | crossLingual | 30 | 10 | 0.353 | 0.983 | 0.427 | 12.5 | 35.2 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | semantic | all | 90 | 10 | 0.676 | 0.728 | 0.608 | 13.9 | 31.6 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | semantic | exact | 30 | 10 | 0.817 | 0.825 | 0.773 | 12.7 | 32.7 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | semantic | paraphrase | 30 | 10 | 0.517 | 0.358 | 0.353 | 13.7 | 15.5 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | semantic | crossLingual | 30 | 10 | 0.694 | 1.000 | 0.698 | 15.1 | 18.2 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | hybrid | all | 90 | 10 | 0.680 | 0.791 | 0.625 | 21.2 | 59.2 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | hybrid | exact | 30 | 10 | 0.933 | 1.000 | 0.938 | 16.8 | 59.2 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | hybrid | paraphrase | 30 | 10 | 0.558 | 0.390 | 0.365 | 19.3 | 62.1 |
| e2-e5-small | multilingual-e5-small | fixedSize 120/15% | hybrid | crossLingual | 30 | 10 | 0.547 | 0.983 | 0.571 | 25.4 | 48.2 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | semantic | all | 90 | 10 | 0.667 | 0.751 | 0.614 | 43.1 | 54.7 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | semantic | exact | 30 | 10 | 0.822 | 0.856 | 0.779 | 40.1 | 44.1 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | semantic | paraphrase | 30 | 10 | 0.533 | 0.414 | 0.402 | 42.4 | 49.9 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | semantic | crossLingual | 30 | 10 | 0.645 | 0.983 | 0.661 | 48.3 | 59.5 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | hybrid | all | 90 | 10 | 0.654 | 0.801 | 0.627 | 55.2 | 114.5 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | hybrid | exact | 30 | 10 | 0.894 | 0.983 | 0.910 | 46.3 | 121.7 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | hybrid | paraphrase | 30 | 10 | 0.525 | 0.436 | 0.404 | 55.0 | 125.6 |
| e2-e5-base | multilingual-e5-base | fixedSize 120/15% | hybrid | crossLingual | 30 | 10 | 0.543 | 0.983 | 0.567 | 68.8 | 103.4 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | semantic | all | 90 | 10 | 0.685 | 0.618 | 0.605 | 18.9 | 23.9 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | semantic | exact | 30 | 10 | 0.806 | 0.689 | 0.684 | 17.7 | 19.5 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | semantic | paraphrase | 30 | 10 | 0.350 | 0.182 | 0.218 | 18.9 | 22.3 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | semantic | crossLingual | 30 | 10 | 0.901 | 0.983 | 0.912 | 21.3 | 24.3 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | hybrid | all | 90 | 10 | 0.770 | 0.761 | 0.691 | 29.7 | 88.5 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | hybrid | exact | 30 | 10 | 0.967 | 0.944 | 0.916 | 22.9 | 104.0 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | hybrid | paraphrase | 30 | 10 | 0.450 | 0.354 | 0.295 | 29.7 | 99.6 |
| e2-minilm | paraphrase-multilingual-MiniLM-L12-v2 | fixedSize 120/15% | hybrid | crossLingual | 30 | 10 | 0.894 | 0.983 | 0.863 | 40.5 | 70.6 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | semantic | all | 90 | 10 | 0.718 | 0.636 | 0.621 | 31.1 | 38.0 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | semantic | exact | 30 | 10 | 0.764 | 0.649 | 0.650 | 29.4 | 31.9 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | semantic | paraphrase | 30 | 10 | 0.517 | 0.357 | 0.353 | 31.6 | 35.2 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | semantic | crossLingual | 30 | 10 | 0.872 | 0.903 | 0.860 | 34.3 | 40.2 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | hybrid | all | 90 | 10 | 0.734 | 0.754 | 0.677 | 39.0 | 68.8 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | hybrid | exact | 30 | 10 | 0.939 | 0.936 | 0.903 | 32.0 | 72.2 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | hybrid | paraphrase | 30 | 10 | 0.433 | 0.367 | 0.317 | 38.1 | 74.9 |
| e2-mpnet | paraphrase-multilingual-mpnet-base-v2 | fixedSize 120/15% | hybrid | crossLingual | 30 | 10 | 0.831 | 0.958 | 0.812 | 45.8 | 65.5 |
