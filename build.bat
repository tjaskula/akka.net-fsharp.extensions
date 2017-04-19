@echo off
cls
".\packages\NuGet.CommandLine\tools\NuGet.exe" "Install" "FAKE" "-OutputDirectory" "packages/"
".\packages\FAKE\tools\Fake.exe" build.fsx
pause