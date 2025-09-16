@echo off

pushd "%~dp0"

dotnet publish Resty.Cli/Resty.Cli.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o ./publish/

if %ERRORLEVEL% EQU 0 (
  echo.
  echo Publish succeeded.

  if exist "%UserProfile%\Bin\resty.exe" (
    echo Copying to %UserProfile%\Bin\resty.exe
    copy /Y ".\publish\resty.exe" "%UserProfile%\Bin\resty.exe"
  )

  popd
  exit /B 0
)

echo.
echo Publish failed.
popd
pause
exit /B 1
