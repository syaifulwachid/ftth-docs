@echo off
title FTTH Design Planner - Build Static Documentation
echo ===================================================
echo Mengompilasi Dokumentasi ke Format HTML Statis...
echo ===================================================
if not exist ".venv\Scripts\mkdocs.exe" (
    echo Menginstal dependensi...
    .\.venv\Scripts\pip.exe install -r requirements-docs.txt
)
.\.venv\Scripts\mkdocs.exe build
echo.
echo ===================================================
echo Selesai! File web statis tersimpan di folder 'site/'
echo ===================================================
pause
