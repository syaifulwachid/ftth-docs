@echo off
title FTTH Design Planner - Local Documentation Server
echo ===================================================
echo Memulai Server Dokumentasi FTTH Design Planner...
echo ===================================================
if not exist ".venv\Scripts\python.exe" (
    echo Menyiapkan virtual environment Python...
    python -m venv .venv
    .\.venv\Scripts\pip.exe install -r requirements-docs.txt
)
echo Membuka server di http://127.0.0.1:8000 ...
.\.venv\Scripts\mkdocs.exe serve
pause
