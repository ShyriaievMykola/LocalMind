# Оцінка пошуку: open-eval

## multilingual-e5-small, fixedSize, фрагмент 128 токенів, перекриття 0%

- Документів: 902, фрагментів: 22482
- Індексація: 968.2 с
- Розмір бази: 74.85 МБ
- Пікова пам'ять: 1494 МБ

## multilingual-e5-small, fixedSize, фрагмент 128 токенів, перекриття 15%

- Документів: 902, фрагментів: 25307
- Індексація: 1050.2 с
- Розмір бази: 83.62 МБ
- Пікова пам'ять: 1567 МБ

## multilingual-e5-small, fixedSize, фрагмент 256 токенів, перекриття 0%

- Документів: 902, фрагментів: 11259
- Індексація: 712.0 с
- Розмір бази: 49.34 МБ
- Пікова пам'ять: 1567 МБ

## multilingual-e5-small, fixedSize, фрагмент 256 токенів, перекриття 15%

- Документів: 902, фрагментів: 12782
- Індексація: 806.0 с
- Розмір бази: 56.85 МБ
- Пікова пам'ять: 1567 МБ

## multilingual-e5-small, fixedSize, фрагмент 512 токенів, перекриття 0%

- Документів: 902, фрагментів: 5818
- Індексація: 855.4 с
- Розмір бази: 41.48 МБ
- Пікова пам'ять: 1593 МБ

## multilingual-e5-small, fixedSize, фрагмент 512 токенів, перекриття 15%

- Документів: 902, фрагментів: 6569
- Індексація: 987.2 с
- Розмір бази: 46.83 МБ
- Пікова пам'ять: 1593 МБ

| Прогін | Модель | Нарізка | Режим | Тип запитів | Запитів | k | Recall@k | MRR | nDCG@k | p50, мс | p95, мс |
|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | semantic | all | 90 | 10 | 0.653 | 0.736 | 0.614 | 24.4 | 98.9 |
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | semantic | exact | 30 | 10 | 0.758 | 0.811 | 0.737 | 24.2 | 104.2 |
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | semantic | paraphrase | 30 | 10 | 0.492 | 0.413 | 0.384 | 22.0 | 47.5 |
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | semantic | crossLingual | 30 | 10 | 0.709 | 0.983 | 0.720 | 27.2 | 98.9 |
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | hybrid | all | 90 | 10 | 0.676 | 0.750 | 0.613 | 38.2 | 78.2 |
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | hybrid | exact | 30 | 10 | 0.944 | 0.983 | 0.938 | 35.6 | 106.5 |
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | hybrid | paraphrase | 30 | 10 | 0.483 | 0.284 | 0.300 | 40.7 | 99.7 |
| e3-fixed-128-overlap0 | multilingual-e5-small | fixedSize 128/0% | hybrid | crossLingual | 30 | 10 | 0.600 | 0.983 | 0.601 | 43.7 | 76.4 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | semantic | all | 90 | 10 | 0.670 | 0.715 | 0.601 | 17.1 | 20.6 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | semantic | exact | 30 | 10 | 0.789 | 0.822 | 0.745 | 16.1 | 17.8 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | semantic | paraphrase | 30 | 10 | 0.525 | 0.345 | 0.350 | 17.1 | 19.4 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | semantic | crossLingual | 30 | 10 | 0.695 | 0.978 | 0.707 | 18.6 | 21.8 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | hybrid | all | 90 | 10 | 0.659 | 0.775 | 0.601 | 24.9 | 65.5 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | hybrid | exact | 30 | 10 | 0.922 | 1.000 | 0.932 | 19.7 | 73.7 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | hybrid | paraphrase | 30 | 10 | 0.500 | 0.340 | 0.292 | 26.0 | 71.8 |
| e3-fixed-128-overlap15 | multilingual-e5-small | fixedSize 128/15% | hybrid | crossLingual | 30 | 10 | 0.555 | 0.983 | 0.578 | 28.4 | 53.0 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | semantic | all | 90 | 10 | 0.642 | 0.733 | 0.599 | 14.6 | 19.2 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | semantic | exact | 30 | 10 | 0.819 | 0.834 | 0.779 | 13.6 | 15.9 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | semantic | paraphrase | 30 | 10 | 0.450 | 0.365 | 0.347 | 14.1 | 16.3 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | semantic | crossLingual | 30 | 10 | 0.656 | 1.000 | 0.670 | 16.3 | 20.1 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | hybrid | all | 90 | 10 | 0.671 | 0.734 | 0.588 | 21.4 | 58.2 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | hybrid | exact | 30 | 10 | 0.961 | 0.958 | 0.920 | 17.2 | 60.0 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | hybrid | paraphrase | 30 | 10 | 0.500 | 0.260 | 0.287 | 21.0 | 59.5 |
| e3-fixed-256-overlap0 | multilingual-e5-small | fixedSize 256/0% | hybrid | crossLingual | 30 | 10 | 0.551 | 0.983 | 0.557 | 28.2 | 43.7 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | semantic | all | 90 | 10 | 0.704 | 0.756 | 0.634 | 15.2 | 19.4 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | semantic | exact | 30 | 10 | 0.883 | 0.909 | 0.854 | 13.8 | 17.1 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | semantic | paraphrase | 30 | 10 | 0.558 | 0.359 | 0.369 | 15.2 | 18.6 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | semantic | crossLingual | 30 | 10 | 0.671 | 1.000 | 0.681 | 16.4 | 19.8 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | hybrid | all | 90 | 10 | 0.668 | 0.757 | 0.601 | 24.6 | 62.4 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | hybrid | exact | 30 | 10 | 0.944 | 1.000 | 0.950 | 18.8 | 67.3 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | hybrid | paraphrase | 30 | 10 | 0.517 | 0.287 | 0.297 | 24.7 | 62.4 |
| e3-fixed-256-overlap15 | multilingual-e5-small | fixedSize 256/15% | hybrid | crossLingual | 30 | 10 | 0.544 | 0.983 | 0.555 | 30.3 | 57.5 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | semantic | all | 90 | 10 | 0.662 | 0.708 | 0.582 | 14.0 | 16.7 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | semantic | exact | 30 | 10 | 0.850 | 0.813 | 0.776 | 12.8 | 15.1 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | semantic | paraphrase | 30 | 10 | 0.508 | 0.333 | 0.324 | 13.9 | 16.3 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | semantic | crossLingual | 30 | 10 | 0.627 | 0.978 | 0.645 | 15.4 | 18.9 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | hybrid | all | 90 | 10 | 0.670 | 0.773 | 0.597 | 19.4 | 45.2 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | hybrid | exact | 30 | 10 | 0.967 | 0.983 | 0.938 | 14.5 | 51.3 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | hybrid | paraphrase | 30 | 10 | 0.508 | 0.353 | 0.313 | 18.9 | 51.5 |
| e3-fixed-512-overlap0 | multilingual-e5-small | fixedSize 512/0% | hybrid | crossLingual | 30 | 10 | 0.534 | 0.983 | 0.539 | 25.8 | 40.3 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | semantic | all | 90 | 10 | 0.703 | 0.774 | 0.635 | 12.9 | 16.6 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | semantic | exact | 30 | 10 | 0.883 | 0.884 | 0.839 | 12.5 | 13.5 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | semantic | paraphrase | 30 | 10 | 0.575 | 0.454 | 0.411 | 12.9 | 14.3 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | semantic | crossLingual | 30 | 10 | 0.651 | 0.983 | 0.656 | 14.3 | 18.5 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | hybrid | all | 90 | 10 | 0.674 | 0.754 | 0.596 | 19.0 | 47.6 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | hybrid | exact | 30 | 10 | 0.944 | 0.983 | 0.932 | 14.4 | 47.6 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | hybrid | paraphrase | 30 | 10 | 0.517 | 0.296 | 0.296 | 18.5 | 48.6 |
| e3-fixed-512-overlap15 | multilingual-e5-small | fixedSize 512/15% | hybrid | crossLingual | 30 | 10 | 0.560 | 0.983 | 0.560 | 23.6 | 43.6 |
