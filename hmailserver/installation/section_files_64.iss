[Files]
; Main server
Source: "..\source\server\hMailServer\x64\Release\hMailServer.exe"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;
Source: "..\source\server\hMailServer\x64\Release\hMailServer.tlb"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;
Source: "..\source\server\hMailServer\x64\Release\hMailServer.Minidump.exe"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server;
; The Visual C++ runtime - see VCREDIST_PATH in hMailServer64.iss.
;
; Only the three that are actually loaded. Everything in Bin resolves to these: hMailServer.exe
; and hMailServer.Minidump.exe import all three, libcrypto, libssl and libpq import
; vcruntime140.dll, and the rest is managed code with no native imports at all. Neither exe
; delay-loads anything, and msvcp140.dll depends on the two vcruntime DLLs and nothing else -
; the msvcp140_* satellites depend on it, never the other way round.
;
; The rest of the redistributable is left out: concrt140.dll, vccorlib140.dll (C++/CX, which
; cannot even load below Windows 8), vcruntime140_threads.dll and the msvcp140_1, _2,
; _atomic_wait and _codecvt_ids satellites. That saves about 1.1 MB.
;
; Which satellites are needed follows from the code, not from the toolset: std::atomic::wait
; and std::counting_semaphore pull in msvcp140_atomic_wait.dll, PPL pulls in concrt140.dll,
; and so on. Such a reference would build and test clean here and then fail to start on a
; machine without the redistributable installed, so re-check this list with
; "dumpbin /imports" when adding a dependency or moving to a newer toolset.
Source: "{#VCREDIST_PATH}\msvcp140.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;
Source: "{#VCREDIST_PATH}\vcruntime140.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;
Source: "{#VCREDIST_PATH}\vcruntime140_1.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;

Source: "SQLCE\SSCERuntime_x64-ENU.msi"; Flags: deleteafterinstall ; Excludes: ".svn"; DestDir: "{tmp}"; Components: server;

; Common tools. They are .NET 10 applications: each is an .exe host with its .dll, .deps.json,
; .runtimeconfig.json and .dll.config beside it.
Source: "..\source\tools\Administrator\bin\x64\Release\hMailAdmin.*"; Excludes: "*.pdb"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: admintools;
Source: "..\source\tools\DBUpdater\Bin\x64\Release\DBUpdater.*"; Excludes: "*.pdb"; DestDir: "{app}\Bin";  Flags: ignoreversion; Components: server;
Source: "..\source\tools\DBSetup\Bin\x64\Release\DBSetup.*"; Excludes: "*.pdb"; DestDir: "{app}\Bin";Flags: ignoreversion;Components: server;
; System.ServiceProcess.ServiceController is a NuGet package on .NET; DBSetup uses it.
Source: "..\source\tools\DBSetup\Bin\x64\Release\System.ServiceProcess.ServiceController.dll"; DestDir: "{app}\Bin";Flags: ignoreversion;Components: server;
Source: "..\source\tools\DBSetup\Bin\x64\Release\runtimes\*"; DestDir: "{app}\Bin\runtimes";Flags: ignoreversion recursesubdirs;Components: server;
Source: "..\Source\tools\DBSetupQuick\bin\x64\release\DBSetupQuick.*"; Excludes: "*.pdb"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server;
Source: "..\source\tools\Administrator\bin\x64\Release\Interop.hMailServer.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;
Source: "..\source\tools\shared\bin\x64\Release\Shared.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;
Source: "..\source\tools\shared\bin\x64\Release\Shared.dll.config"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;

; The .NET Desktop Runtime the tools need, extracted only when it is not installed yet and
; run by InstallDotNetDesktopRuntime() before any of the tools is started.
Source: "{#DOTNET_DESKTOP_RUNTIME_PATH}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsDotNetDesktopRuntime; Components: server admintools;

; Data directory synchronizer
; Its build output already holds Interop.hMailServer.dll and Shared.dll.
Source: "..\source\Tools\DataDirectorySynchronizer\Bin\x64\Release\*"; Excludes: "*.pdb"; DestDir: "{app}\Addons\DataDirectorySynchronizer"; Flags: ignoreversion recursesubdirs; Components: server;

; OpenSSL
Source: "{#OPENSSL_LIBS_PATH}\libcrypto-3-x64.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;
Source: "{#OPENSSL_LIBS_PATH}\libssl-3-x64.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;

; PQSQL (PostgreSQL client)
Source: "{#POSTGRESQL_LIBPQ_PATH}\*.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;

; MariaDB Connector/C (MySQL / MariaDB client)
Source: "{#MARIADB_LIBS_PATH}\libmariadb.dll"; DestDir: "{app}\Bin"; Flags: ignoreversion; Components: server admintools;