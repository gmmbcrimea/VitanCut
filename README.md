# Vitan-Cut

Нативное Windows-приложение для расчёта мебели: проекты, изделия, деталировка, материалы, раскрой листовых материалов и расчёт стоимости.

## Локальные релизы

Текущая версия: **1.2.8**. Запуск: `release/1.2.8/VitanCut.WinUI.exe`.
Предыдущие сборки сохранены в `release/1.1.1/` и `release/1.2.0/`. Новая сборка включает .NET runtime.
Исходники версии 1.2.8 находятся в этом репозитории; история Git сохранена.

Повторная сборка: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1`.
Core checks требуют отдельный `VITANCUT_TEST_ROOT`, например `.test-runtime/checks`.
Для изолированной проверки задайте `VITANCUT_ENVIRONMENT=Development` и `VITANCUT_DATA_ROOT` вне папки executable.
Текущие изменения перечислены в `release-notes/v1.2.8.md`.
Миграция `supabase/migrations/2026101201_secure_and_seed_delta_workspaces.sql` сохранена в исходниках; автоматически на сервер не применялась.

## Возможности

- проекты с изделиями, контрагентами, адресами и зарплатой;
- деталировка с формулами размеров, понятным отображением выражений и блокировкой поворота деталей;
- каталог материалов, расчёт себестоимости и общей стоимости проекта;
- интерактивная карта кроя ЛДСП с четырьмя вариантами оптимизации, ручным редактированием, групповой работой с деталями и полезными остатками;
- линии реальных резов, подсчёт их количества и длины с учётом подрезки;
- отдельный дополнительный раскрой для стекла, ДВП и других не-ЛДСП материалов;
- экспорт деталировки и раскроя в PDF или XLSX;
- тёмная, светлая и системная темы, палитры акцентов и настройки оформления;
- облачная синхронизация рабочей базы через Supabase;
- проверка обновлений и установка новых portable-версий через GitHub Releases.

## Portable-версия

Скачайте архив из [Releases](https://github.com/gmmbcrimea/VitanCut/releases), распакуйте его в любую доступную для записи папку и запустите `VitanCut.WinUI.exe`.

Portable-версия хранит данные в своей папке `data`; при установке в Program Files используется `%LOCALAPPDATA%\VitanCut\data`. Перед заменой версии сохраняйте незавершённую работу и резервную копию базы. Приложение также умеет импортировать и экспортировать базу в JSON из раздела настроек.

## Сборка из исходников

Требования:

- Windows 10/11;
- .NET SDK 8;
- Visual Studio с workload `Windows application development` или установленный Windows App SDK.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1
$env:VITANCUT_TEST_ROOT = Join-Path (Get-Location) '.test-runtime/checks'
dotnet run --project native-winui-tests/VitanCut.CoreChecks.csproj -c Release
```

Готовая release-сборка находится в `release/1.2.8/`.

Для локальной portable-упаковки:

```powershell
powershell -ExecutionPolicy Bypass -File native-winui/package-portable.ps1 -Version 1.2.8
```

## Публикация обновления

1. Обновите номер версии в `native-winui/VitanCut.WinUI.csproj`.
2. Создайте и отправьте тег вида `v1.0.2`.
3. GitHub Actions соберёт Windows portable-архив и создаст GitHub Release автоматически.

При запуске приложение сверяет свою версию с последним GitHub Release. Если опубликована более новая portable-версия, пользователь увидит уведомление и сможет установить её одной кнопкой.

## Структура

- `native-winui/` — основное приложение WinUI 3;
- `native-winui-tests/` — регрессионные проверки расчётов и раскроя;
- `supabase/migrations/` — миграции облачной синхронизации;
- `.github/workflows/release.yml` — автоматическая публикация portable-релизов.
