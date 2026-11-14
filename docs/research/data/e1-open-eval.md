# Оцінка пошуку: open-eval

## multilingual-e5-small, fixedSize, фрагмент 256 токенів, перекриття 15%

- Документів: 902, фрагментів: 12782
- Індексація: 1292.7 с
- Розмір бази: 56.59 МБ
- Пікова пам'ять: 1494 МБ

| Прогін | Модель | Нарізка | Режим | Тип запитів | Запитів | k | Recall@k | MRR | nDCG@k | p50, мс | p95, мс |
|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| cli | multilingual-e5-small | fixedSize 256/15% | keyword | all | 90 | 10 | 0.520 | 0.719 | 0.503 | 19.2 | 67.2 |
| cli | multilingual-e5-small | fixedSize 256/15% | keyword | exact | 30 | 10 | 0.906 | 1.000 | 0.931 | 13.9 | 66.7 |
| cli | multilingual-e5-small | fixedSize 256/15% | keyword | paraphrase | 30 | 10 | 0.317 | 0.157 | 0.158 | 20.4 | 73.0 |
| cli | multilingual-e5-small | fixedSize 256/15% | keyword | crossLingual | 30 | 10 | 0.337 | 1.000 | 0.420 | 24.8 | 67.2 |
| cli | multilingual-e5-small | fixedSize 256/15% | semantic | all | 90 | 10 | 0.704 | 0.756 | 0.634 | 25.3 | 53.5 |
| cli | multilingual-e5-small | fixedSize 256/15% | semantic | exact | 30 | 10 | 0.883 | 0.909 | 0.854 | 24.5 | 73.1 |
| cli | multilingual-e5-small | fixedSize 256/15% | semantic | paraphrase | 30 | 10 | 0.558 | 0.359 | 0.369 | 23.1 | 53.5 |
| cli | multilingual-e5-small | fixedSize 256/15% | semantic | crossLingual | 30 | 10 | 0.671 | 1.000 | 0.681 | 30.9 | 45.6 |
| cli | multilingual-e5-small | fixedSize 256/15% | hybrid | all | 90 | 10 | 0.666 | 0.757 | 0.601 | 36.5 | 107.7 |
| cli | multilingual-e5-small | fixedSize 256/15% | hybrid | exact | 30 | 10 | 0.933 | 1.000 | 0.949 | 32.3 | 94.8 |
| cli | multilingual-e5-small | fixedSize 256/15% | hybrid | paraphrase | 30 | 10 | 0.517 | 0.289 | 0.297 | 40.0 | 114.4 |
| cli | multilingual-e5-small | fixedSize 256/15% | hybrid | crossLingual | 30 | 10 | 0.549 | 0.983 | 0.558 | 45.5 | 107.5 |
