@echo off
echo === PULLING FROM GIT (FORCE SYNC TO MAIN) ===

git fetch origin
git reset --hard origin/main

echo.
echo Done. Local is now identical to origin/main.
pause