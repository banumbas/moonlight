@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

echo ===================================================
echo     Space Station Classic - Discord Link Bot
echo ===================================================
echo.

cd /d "%~dp0"

:: Check python
where py >nul 2>nul
if %errorlevel% equ 0 (
    set "PY_CMD=py"
) else (
    where python >nul 2>nul
    if %errorlevel% equ 0 (
        set "PY_CMD=python"
    ) else (
        echo [ОШИБКА] Python не найден! Установите Python 3.10+ и добавьте его в PATH.
        pause
        exit /b 1
    )
)

:: Check config (server_config.toml or config.json)
set "HAS_CONFIG=0"
if exist "server_config.toml" set "HAS_CONFIG=1"
if exist "..\..\server_config.toml" set "HAS_CONFIG=1"
if exist "config.json" set "HAS_CONFIG=1"

if "%HAS_CONFIG%"=="0" (
    if exist "config.example.json" (
        echo [ИНФО] Файл конфигурации не найден. Копируем config.example.json...
        copy "config.example.json" "config.json" > nul
        echo [ВНИМАНИЕ] Создан файл config.json!
        echo Укажите настройки в server_config.toml (в корне сервера) или в config.json, затем перезапустите.
        pause
        exit /b 0
    ) else (
        echo [ОШИБКА] Ни server_config.toml, ни config.json не найдены!
        pause
        exit /b 1
    )
)

:: Create venv if not exists
if not exist "venv" (
    echo [ИНФО] Создание виртуального окружения venv...
    %PY_CMD% -m venv venv
    if %errorlevel% neq 0 (
        echo [ОШИБКА] Не удалось создать виртуальное окружение.
        pause
        exit /b 1
    )
)

:: Activate venv
call venv\Scripts\activate.bat

:: Install/update dependencies
echo [ИНФО] Проверка зависимостей...
python -m pip install --upgrade pip >nul 2>nul
python -m pip install -r requirements.txt
if %errorlevel% neq 0 (
    echo [ОШИБКА] Ошибка при установке зависимостей из requirements.txt!
    pause
    exit /b 1
)

echo.
echo [ИНФО] Запуск бота...
echo ===================================================
python bot.py
if %errorlevel% neq 0 (
    echo.
    echo [ОШИБКА] Бот завершил работу с ошибкой.
    pause
)
