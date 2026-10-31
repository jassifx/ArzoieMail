#define HMAILSERVERLIBS = GetEnv("hMailServerLibs")
#define OPENSSL_LIBS_PATH HMAILSERVERLIBS + "\openssl-3.5.9\out64\bin"
#define POSTGRESQL_LIBPQ_PATH HMAILSERVERLIBS + "\postgresql-18.6\Release\libpq"
#define MARIADB_LIBS_PATH HMAILSERVERLIBS + "\libmariadb-3.4.11\build64\libmariadb\RelWithDebInfo"
#define SEVENZIP_PATH HMAILSERVERLIBS + "\7zip-26.03"
; Microsoft's .NET Desktop Runtime installer, fetched by libraries\fetch-dotnet-runtime.ps1.
; The tools are .NET 10 applications; setup installs the runtime when it is missing.
#define DOTNET_DESKTOP_RUNTIME_VERSION "10.0.13"
#define DOTNET_DESKTOP_RUNTIME_FILE "windowsdesktop-runtime-" + DOTNET_DESKTOP_RUNTIME_VERSION + "-win-x64.exe"
#define DOTNET_DESKTOP_RUNTIME_PATH HMAILSERVERLIBS + "\dotnet-desktop-runtime-" + DOTNET_DESKTOP_RUNTIME_VERSION + "\" + DOTNET_DESKTOP_RUNTIME_FILE
; The ASP.NET Core Runtime the web administration panel needs, fetched into the same folder.
#define ASPNETCORE_RUNTIME_FILE "aspnetcore-runtime-" + DOTNET_DESKTOP_RUNTIME_VERSION + "-win-x64.exe"
#define ASPNETCORE_RUNTIME_PATH HMAILSERVERLIBS + "\dotnet-desktop-runtime-" + DOTNET_DESKTOP_RUNTIME_VERSION + "\" + ASPNETCORE_RUNTIME_FILE

; The Visual C++ runtime shipped in Bin. Microsoft's advice is to ship the latest available
; runtime, and the installer requires Windows 10, where every 14.x runtime runs, so the
; newest one installed is used. It may not be older than the toolset that compiled
; hMailServer.exe. build\Get-VCRedistPath.ps1 locates it: the build workflow passes it as
; /DVCREDIST_PATH, a local build sets VCRedistPath instead. Windows 10 and later carry the
; Universal CRT themselves, so that is not shipped.
#ifndef VCREDIST_PATH
  #define VCREDIST_PATH GetEnv("VCRedistPath")
#endif
#if VCREDIST_PATH == ""
  #error "VCREDIST_PATH is not set. Set the VCRedistPath environment variable to the output of 'Get-VCRedistPath -Newest', or pass /DVCREDIST_PATH to ISCC."
#endif

#include "section_setup.iss"
#include "section_setup_64.iss"
#include "section_custom_messages.iss"
#include "section_languages.iss"
#include "section_istool.iss"
#include "section_types.iss"
#include "section_components.iss"

#include "section_files_common.iss"

#include "section_files_64.iss"
#include "section_installdelete.iss"

#include "section_messages.iss"
#include "section_ini.iss"
#include "section_dirs.iss"
#include "section_run.iss"
#include "section_uninstallrun.iss"

#include "section_icons.iss"

#include "hMailServerInnoExtension.iss"

