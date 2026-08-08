@echo off
setlocal

pushd "%~dp0" >nul

echo.
echo === Continuum First-Run Setup Quarantined ===
echo The recovered Vanilla Sim wizard is experimental and is not safe for
echo production configuration. It can overwrite simulator configuration and
echo persist setup credentials in plaintext.
echo.
echo Configure OpenSim.ini, Robust.ini, and Regions.ini manually using the
echo supplied examples and deployment documentation.
echo Developers auditing the recovered script may invoke it directly with the
echo explicit -UnsafeExperimental switch in an isolated disposable environment.
echo.
popd >nul
exit /b 1
