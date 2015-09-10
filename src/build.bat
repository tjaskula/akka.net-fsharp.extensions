@echo off
cls
".\packages\NuGet.CommandLine.2.8.6\tools\NuGet.exe" "Install" "FAKE" "-OutputDirectory" "..\packages"
"..\packages\FAKE\tools\Fake.exe" build.fsx
pause