@echo off

IF NOT EXIST packages\FAKE\tools\FAKE.exe  (
  .\packages\NuGet.CommandLine\tools\NuGet.exe install FAKE -OutputDirectory packages -ExcludeVersion
)

IF NOT EXIST build.fsx (
  packages\FAKE\tools\FAKE.exe init.fsx
)
packages\FAKE\tools\FAKE.exe build.fsx %*

pause