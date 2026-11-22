@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo JV-Link(64bit) に全データ種別を少しずつ試します。数分かかります。閉じないでください。
TurfLab.Cli.exe probe --store "%~dp0probe.sqlite" > probe_result.txt 2>&1
type probe_result.txt
echo.
echo 終わりました。probe_result.txt の中身を Claude に貼ってください。
pause
