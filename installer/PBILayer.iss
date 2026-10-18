; Instalador de PBI Layer (Inno Setup 6).
; No se compila a mano: lo arma build-installer.ps1, que pasa la versión (/DAppVersion=...) y genera icon-data.inc.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#include "icon-data.inc"

#define AppName "PBI Layer"
#define AppExe "PBILayer.exe"
#define AppPublisher "Ignacio Simone"
#define AppUrl "https://github.com/IgnacioSimone/PBILayer"

[Setup]
; GUID fijo: es lo que le permite a una versión nueva reemplazar a la anterior en vez de instalarse al lado.
AppId={{592B6185-BF16-446B-857D-FE010307C4BA}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador de {#AppName}
VersionInfoCopyright=© 2026 {#AppPublisher} — Licencia MIT

; Requiere administrador porque registra la herramienta externa en "Program Files (x86)\Common Files",
; que es donde Power BI Desktop busca los archivos .pbitool.json.
#ifdef TestMode
; Solo para probar el instalador en una carpeta aparte sin tocar la instalación real (ver "TestMode" abajo).
PrivilegesRequired=lowest
#else
PrivilegesRequired=admin
#endif
DefaultDirName={autopf}\{#AppName}
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

#ifdef TestMode
OutputDir={#TestMode}
OutputBaseFilename=PBILayer-Setup-test
#else
OutputDir=..\installer_output
OutputBaseFilename=PBILayer-Setup-{#AppVersion}
#endif
SetupIconFile=..\Assets\PBILayer.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; Si PBI Layer está abierto, el instalador lo cierra y (en modo normal) lo vuelve a abrir al terminar.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; La herramienta externa se registra en una carpeta compartida de Power BI Desktop: se limpia al desinstalar.
; (Las preferencias y el registro de errores quedan en %LOCALAPPDATA%\PBILayer a propósito.)
#ifdef TestMode
Type: filesandordirs; Name: "{app}\_external-tools"
#else
Type: files; Name: "{commoncf32}\Microsoft Shared\Power BI Desktop\External Tools\PBILayer.pbitool.json"
Type: filesandordirs; Name: "{localappdata}\PBILayer\updates"
#endif

[Code]
// TestMode (solo pruebas): la "carpeta de Power BI" y la "instalación vieja" viven dentro de la carpeta de
// instalación, así una prueba nunca toca la instalación real ni el registro de la máquina.
function ToolManifestDir(): String;
begin
#ifdef TestMode
  Result := ExpandConstant('{app}\_external-tools');
#else
  Result := ExpandConstant('{commoncf32}\Microsoft Shared\Power BI Desktop\External Tools');
#endif
end;

function LegacyDir(): String;
begin
#ifdef TestMode
  Result := ExpandConstant('{app}\_legacy');
#else
  Result := ExpandConstant('{localappdata}\PBIContext');
#endif
end;

function JsonEscape(const Text: String): String;
begin
  Result := Text;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

{ Registra PBI Layer en la pestaña "Herramientas externas" de Power BI Desktop. }
procedure WriteToolManifest();
var
  Json: String;
begin
  ForceDirectories(ToolManifestDir());
  Json :=
    '{' + #13#10 +
    '  "version": "1.0",' + #13#10 +
    '  "name": "PBI Layer",' + #13#10 +
    '  "description": "Medidas DAX, tablas, columnas, relaciones, Power Query y visuales del modelo abierto — solo metadata, nunca datos. No afiliado a Microsoft.",' + #13#10 +
    '  "path": "' + JsonEscape(ExpandConstant('{app}\{#AppExe}')) + '",' + #13#10 +
    '  "arguments": "\"%server%\" \"%database%\"",' + #13#10 +
    '  "iconData": "{#IconData}"' + #13#10 +
    '}';
  SaveStringToFile(ToolManifestDir() + '\PBILayer.pbitool.json', Json, False);
end;

{ Versiones anteriores se copiaban a mano a %LOCALAPPDATA%\PBILayer (mezcladas con las preferencias y el
  registro). Si hay una de esas, se borran solo sus archivos de programa para no dejar dos copias. }
procedure RemoveLegacyProgramFiles();
var
  Dir, Name: String;
  Find: TFindRec;
begin
  Dir := LegacyDir();
  if not FileExists(Dir + '\PBIContext.dll') then Exit;

  if FindFirst(Dir + '\*', Find) then
  try
    repeat
      Name := Find.Name;
      if (Name = '.') or (Name = '..') then Continue;

      if (Find.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        if (CompareText(Name, 'updates') <> 0) and (CompareText(Name, 'state') <> 0) then
          DelTree(Dir + '\' + Name, True, True, True);
      end
      else if (CompareText(Name, 'settings.json') <> 0) and (CompareText(Name, 'debug.log') <> 0) then
        DeleteFile(Dir + '\' + Name);
    until not FindNext(Find);
  finally
    FindClose(Find);
  end;
end;

{ La app se llamaba "PBI Context" (otro AppId). Si está instalada, se desinstala en silencio antes de
  instalar esta: así no quedan dos accesos directos, dos entradas en Herramientas externas ni dos
  programas en "Aplicaciones instaladas". Las preferencias no se tocan (la app las migra sola). }
procedure RemoveRenamedPredecessor();
var
  Uninstaller: String;
  ResultCode: Integer;
begin
#ifndef TestMode
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8C2D6A1-5E3F-4C7A-9D21-6F0A1E4B7C35}_is1',
       'UninstallString', Uninstaller) or
     RegQueryStringValue(HKLM32, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8C2D6A1-5E3F-4C7A-9D21-6F0A1E4B7C35}_is1',
       'UninstallString', Uninstaller) then
  begin
    Exec(RemoveQuotes(Uninstaller), '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RemoveRenamedPredecessor();
    RemoveLegacyProgramFiles();
  end
  else if CurStep = ssPostInstall then
    WriteToolManifest();
end;
