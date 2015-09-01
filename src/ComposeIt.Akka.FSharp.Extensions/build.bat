
@echo off
cls
"..\packages\NuGet.CommandLine.2.8.6\tools\NuGet.exe" "Install" "FAKE" "-OutputDirectory" "..\..\packages" "-ExcludeVersion"
"..\packages\NuGet.CommandLine.2.8.6\tools\NuGet.exe" "Install" "FAKE.SQL" "-OutputDirectory" "..\..\packages" "-ExcludeVersion"
"..\packages\NuGet.CommandLine.2.8.6\tools\NuGet.exe" "Install" "FAKE.SQL.x64" "-OutputDirectory" "..\..\packages" "-ExcludeVersion"
pause