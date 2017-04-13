@echo off
cls
".\packages\NuGet.CommandLine.3.4.3\tools\NuGet.exe" "Install" "FAKE" "-OutputDirectory" "..\packages"
".\packages\FAKE\tools\Fake.exe" build.fsx
pause