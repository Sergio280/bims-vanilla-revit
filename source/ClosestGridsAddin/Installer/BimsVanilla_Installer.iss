; Script de Inno Setup para BIMS VANILLA - Add-in de Revit 2025
; Creado automáticamente

#define MyAppName "BIMS VANILLA"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SGAZ - 989455558"
#define MyAppURL "https://www.bimsvanilla.com"
#define RevitVersion "2025"

[Setup]
; Información de la aplicación
AppId={{8A5FCC5E-4B6B-4D92-BC99-EA8E70CE0CF5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={commonappdata}\Autodesk\Revit\Addins\{#RevitVersion}\BIMS-VANILLA
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Privilegios de administrador requeridos
PrivilegesRequired=admin
; Configuración del instalador
OutputDir=..\Installer\Output
OutputBaseFilename=BimsVanilla_Revit{#RevitVersion}_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Iconos (opcional - puedes personalizarlos)
; SetupIconFile=..\Resources\icon.ico
; UninstallDisplayIcon={app}\ClosestGridsAddinVANILLA.dll

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; ========================================
; DLL PRINCIPAL (OFUSCADO CON .NET REACTOR)
; ========================================
Source: "..\bin\Release R25\ClosestGridsAddinVANILLA.dll"; DestDir: "{app}"; Flags: ignoreversion

; ========================================
; DEPENDENCIAS EXTERNAS (SOLO LAS NECESARIAS)
; ========================================
; Firebase y dependencias
Source: "..\bin\Release R25\FireSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\System.Net.Http.Extensions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\System.Net.Http.Primitives.dll"; DestDir: "{app}"; Flags: ignoreversion

; Threading
Source: "..\bin\Release R25\Microsoft.Threading.Tasks.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\Microsoft.Threading.Tasks.Extensions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\Microsoft.Threading.Tasks.Extensions.Desktop.dll"; DestDir: "{app}"; Flags: ignoreversion

; Aspose Cells (para Excel)
Source: "..\bin\Release R25\Aspose.Cells.dll"; DestDir: "{app}"; Flags: ignoreversion

; Nice3point
Source: "..\bin\Release R25\Nice3point.Revit.Extensions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\Nice3point.Revit.Toolkit.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\JetBrains.Annotations.dll"; DestDir: "{app}"; Flags: ignoreversion

; System
Source: "..\bin\Release R25\System.CodeDom.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\System.Management.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release R25\System.Security.Cryptography.Pkcs.dll"; DestDir: "{app}"; Flags: ignoreversion

; ❌ NO COPIAR: RevitAPI.dll, RevitAPIUI.dll (ya están en Revit)
; ❌ NO COPIAR: Carpetas de ofuscación (ClosestGridsAddinVANILLA_Secure, Obfuscator_Output)

; ========================================
; RECURSOS (ICONOS PNG)
; ========================================
Source: "..\bin\Release R25\Resources\*.png"; DestDir: "{app}\Resources"; Flags: ignoreversion

; ========================================
; ARCHIVO .ADDIN
; ========================================
Source: "..\bin\Release R25\ClosestGridsAddinVANILLA.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\{#RevitVersion}"; Flags: ignoreversion

[Icons]
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  // Verificar si Revit está en ejecución
  if CheckForMutexes('RevitServerToolMutex') then
  begin
    if MsgBox('Revit parece estar en ejecución. Se recomienda cerrarlo antes de continuar.' + #13#10 +
              '¿Desea continuar de todos modos?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Aquí puedes agregar acciones post-instalación si es necesario
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Messages]
spanish.WelcomeLabel1=Bienvenido al Asistente de Instalación de [name]
spanish.WelcomeLabel2=Este asistente instalará [name/ver] en su computadora.%n%nEste add-in funciona con Autodesk Revit {#RevitVersion}.%n%nSe recomienda cerrar Revit antes de continuar.
english.WelcomeLabel1=Welcome to the [name] Setup Wizard
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nThis add-in works with Autodesk Revit {#RevitVersion}.%n%nIt is recommended that you close Revit before continuing.
