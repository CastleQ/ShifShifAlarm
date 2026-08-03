@echo off
chcp 65001 > nul
echo [쉽쉽알람] 백업 시작...
git add -A
git commit -m "backup %date% %time%"
git push
echo [쉽쉽알람] 백업 완료!