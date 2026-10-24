[InstallDelete]
; Earlier versions shipped the Universal CRT and the 14.29 runtime for Windows versions older
; than 10. Windows 10 and later carry the Universal CRT themselves and never load these
; copies, so an upgrade removes them rather than leaving them behind in Bin.
Type: files; Name: "{app}\Bin\api-ms-win-*.dll"
Type: files; Name: "{app}\Bin\ucrtbase.dll"
; The .NET Framework builds of the tools read <tool>.exe.config; the .NET 10 builds read
; <tool>.dll.config, so an upgrade removes the old files.
Type: files; Name: "{app}\Bin\*.exe.config"
Type: files; Name: "{app}\Addons\DataDirectorySynchronizer\*.exe.config"
