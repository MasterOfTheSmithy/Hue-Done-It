@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ===== CONFIG =====
set "REMOTE_URL=https://github.com/MasterOfTheSmithy/Hue-Done-It"
set "DEFAULT_MSG=chore: update project state"
set "LOG=%~dp0commit_push_log.txt"

echo ===== %DATE% %TIME% =====> "%LOG%"

echo STEP 1: Starting in %CD%>> "%LOG%"

cd /d "%~dp0" >> "%LOG%" 2>&1
echo STEP 2: cd done (err=%ERRORLEVEL%) in %CD%>> "%LOG%"

REM --- Verify git exists ---
where git >> "%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: git not found in PATH>> "%LOG%"
  echo ERROR: git not found. Install Git for Windows.
  pause
  exit /b 1
)

echo STEP 3: git found>> "%LOG%"

REM --- Verify inside git repo ---
git rev-parse --is-inside-work-tree >> "%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: Not a git repo/worktree at %CD%>> "%LOG%"
  echo ERROR: Not a git repo/worktree here: %CD%
  pause
  exit /b 1
)

echo STEP 4: confirmed inside git work tree>> "%LOG%"

REM --- Ensure remote exists ---
git remote get-url origin >> "%LOG%" 2>&1
if errorlevel 1 (
  echo STEP 4b: origin not found, adding remote>> "%LOG%"
  git remote add origin "%REMOTE_URL%" >> "%LOG%" 2>&1
)

REM --- Status ---
echo STEP 5: git status>> "%LOG%"
git status >> "%LOG%" 2>&1
git status

REM --- Stage ---
echo STEP 6: git add .>> "%LOG%"
git add . >> "%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: git add failed>> "%LOG%"
  echo ERROR: git add failed. See log: %LOG%
  pause
  exit /b 1
)

REM --- Commit message (optional override) ---
set "MSG=%DEFAULT_MSG%"
if not "%~1"=="" (
  set "MSG=%*"
)

echo STEP 7: git commit>> "%LOG%"
git commit -m "%MSG%" >> "%LOG%" 2>&1

echo STEP 7b: commit exit code=%ERRORLEVEL%>> "%LOG%"

REM --- Determine current branch ---
for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD') do set "BR=%%B"
echo STEP 8: branch=%BR%>> "%LOG%"

REM --- Push ---
echo STEP 9: pushing to origin/%BR%>> "%LOG%"
git push >> "%LOG%" 2>&1

if errorlevel 1 (
  echo STEP 9b: push failed, trying explicit upstream>> "%LOG%"
  git push -u origin "%BR%" >> "%LOG%" 2>&1

  if errorlevel 1 (
    echo ERROR: git push failed>> "%LOG%"
    echo ERROR: git push failed. Open log: %LOG%
    pause
    exit /b 1
  )
)

echo SUCCESS: Commit+Push complete>> "%LOG%"
echo SUCCESS: Commit+Push complete
pause
exit /b 0