@echo off
cls
".\packages\NuGet.CommandLine\tools\NuGet.exe" "Install" "FAKE" "-OutputDirectory" "packages/"
".\packages\FAKE.4.61.0\tools\Fake.exe" build.fsx
pause