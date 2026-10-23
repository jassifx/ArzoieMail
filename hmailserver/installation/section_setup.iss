[Setup]
; AppId is what Windows and Setup recognise an installation by. It stays hMailServer, so
; that this installer upgrades an existing hMailServer installation in place (keeping its
; folder and its Programs and Features entry) rather than installing beside it.
AppId=hMailServer
AppName=ArzoieMail
AppPublisher=ArzoieMail
DefaultDirName={commonpf}\ArzoieMail
DefaultGroupName=ArzoieMail
PrivilegesRequired=admin
SolidCompression=yes
WizardImageFile=setup.bmp
LicenseFile=license.rtf
AllowNoIcons=yes
Uninstallable=true
DirExistsWarning=no
CreateAppDir=true
; Windows 10 / Windows Server 2016. hMailServer.exe imports libpq.dll, and libpq from
; PostgreSQL 16 and later requires Windows 10, so on anything older the service would not
; start at all - whichever database it is configured for.
MinVersion=10.0
