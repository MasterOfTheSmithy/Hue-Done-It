@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM =========================================================
REM Commit + Push current repo state to GitHub
REM Usage:
REM   commit_push.bat
REM   commit_push.bat fix movement and splatter pass
REM =========================================================

REM ===== CONFIG =====
set "REMOTE_URL=https://github.com/MasterOfTheSmithy/Hue-Done-It.git"
set "DEFAULT_MSG=chore: update project state"
set "LOG=%~dp0commit_push_log.txt"

echo ===== %DATE% %TIME% =====> "%LOG%"
echo STEP 1: starting in %CD%>> "%LOG%"

cd /d "%~dp0" >> "%LOG%" 2>&1
echo STEP 2: cd done (err=%ERRORLEVEL%) in %CD%>> "%LOG%"

REM --- Verify git exists ---
where git >> "%LOG%" 2>&1
if errorlevel 1 (
    echo ERROR: git not found in PATH>> "%LOG%"
    echo ERROR: git not found. Install Git for Windows and make sure git is in PATH.
    pause
    exit /b 1
)

echo STEP 3: git found>> "%LOG%"

REM --- Verify inside git repo ---
git rev-parse --is-inside-work-tree >> "%LOG%" 2>&1
if errorlevel 1 (
    echo ERROR: not a git repo/worktree at %CD%>> "%LOG%"
    echo ERROR: not a git repo/worktree here: %CD%
    pause
    exit /b 1
)

echo STEP 4: confirmed inside git work tree>> "%LOG%"

REM --- Ensure origin exists and points to expected repo ---
git remote get-url origin >nul 2>> "%LOG%"
if errorlevel 1 (
    echo STEP 5: origin missing, adding remote>> "%LOG%"
    git remote add origin "%REMOTE_URL%" >> "%LOG%" 2>&1
    if errorlevel 1 (
        echo ERROR: failed to add origin remote>> "%LOG%"
        echo ERROR: failed to add origin remote. See log: %LOG%
        pause
        exit /b 1
    )
) else (
    for /f "delims=" %%R in ('git remote get-url origin 2^>nul') do set "CURRENT_REMOTE=%%R"
    echo STEP 5: current origin=!CURRENT_REMOTE!>> "%LOG%"
    if /I not "!CURRENT_REMOTE!"=="%REMOTE_URL%" (
        echo STEP 5b: resetting origin url to expected remote>> "%LOG%"
        git remote set-url origin "%REMOTE_URL%" >> "%LOG%" 2>&1
        if errorlevel 1 (
            echo ERROR: failed to set origin remote url>> "%LOG%"
            echo ERROR: failed to set origin remote url. See log: %LOG%
            pause
            exit /b 1
        )
    )
)

REM --- Determine current branch ---
for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "BR=%%B"
if "%BR%"=="" (
    echo ERROR: failed to determine current branch>> "%LOG%"
    echo ERROR: failed to determine current branch.
    pause
    exit /b 1
)
echo STEP 6: branch=%BR%>> "%LOG%"

REM --- Optional commit message override ---
set "MSG=%DEFAULT_MSG%"
if not "%~1"=="" (
    set "MSG=%*"
)
echo STEP 7: commit message=%MSG%>> "%LOG%"

REM --- Show status before staging ---
echo STEP 8: git status --short before add>> "%LOG%"
git status --short >> "%LOG%" 2>&1
git status --short

REM --- Stage everything ---
echo STEP 9: git add -A>> "%LOG%"
git add -A >> "%LOG%" 2>&1
if errorlevel 1 (
    echo ERROR: git add -A failed>> "%LOG%"
    echo ERROR: git add failed. See log: %LOG%
    pause
    exit /b 1
)

REM --- Detect whether anything is staged/changed ---
git diff --cached --quiet
set "HAS_STAGED=%ERRORLEVEL%"

git diff --quiet
set "HAS_UNSTAGED=%ERRORLEVEL%"

echo STEP 10: staged_diff_exit=%HAS_STAGED% unstaged_diff_exit=%HAS_UNSTAGED%>> "%LOG%"

REM If nothing staged and nothing unstaged, there may still be untracked files already added.
REM Safer check: porcelain output after add.
for /f %%C in ('git status --porcelain ^| find /c /v ""') do set "STATUS_COUNT=%%C"
echo STEP 10b: porcelain_count=%STATUS_COUNT%>> "%LOG%"

if "%STATUS_COUNT%"=="0" (
    echo STEP 11: nothing to commit, attempting push only>> "%LOG%"
    echo Nothing to commit. Attempting push...
) else (
    echo STEP 11: git commit -m "%MSG%">> "%LOG%"
    git commit -m "%MSG%" >> "%LOG%" 2>&1
    set "COMMIT_EXIT=%ERRORLEVEL%"
    echo STEP 11b: commit exit code=%COMMIT_EXIT%>> "%LOG%"

    if not "%COMMIT_EXIT%"=="0" (
        echo ERROR: git commit failed>> "%LOG%"
        echo ERROR: git commit failed. See log: %LOG%
        pause
        exit /b 1
    )
)

REM --- Fetch remote info first ---
echo STEP 12: git fetch origin>> "%LOG%"
git fetch origin >> "%LOG%" 2>&1

REM --- Push current branch ---
echo STEP 13: git push origin %BR%>> "%LOG%"
git push origin "%BR%" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo STEP 13b: push failed, trying upstream set>> "%LOG%"
    git push -u origin "%BR%" >> "%LOG%" 2>&1
    if errorlevel 1 (
        echo ERROR: git push failed>> "%LOG%"
        echo ERROR: git push failed. Open log: %LOG%
        pause
        exit /b 1
    )
)

echo STEP 14: final status>> "%LOG%"
git status --short >> "%LOG%" 2>&1

echo SUCCESS: commit/push complete for branch %BR%>> "%LOG%"
echo SUCCESS: commit/push complete for branch %BR%
echo Log: %LOG%
pause
exit /b 0