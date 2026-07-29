; Branch Merger - first-install wizard.
;
; This owns the entire *visible* install experience (welcome, folder-selection, progress,
; finish), then runs Velopack's Setup.exe SILENTLY to do the real install into the chosen
; folder. So the user sees one cohesive installer and never a second (Velopack) window,
; while Velopack's in-app auto-update keeps working afterwards.
;
; Per-user install (no admin) into a user-writable location, so Velopack's later updates
; stay silent. Uninstall is intentionally handled by Velopack (Windows Apps & features,
; with the keep/remove-data prompt) - this wizard does not register its own uninstaller.
;
; Built by pack.ps1 / release.sh, which pass the version:  ISCC /DMyVersion=x.y.z

#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif

[Setup]
AppId={{B7A3E7C2-6E4B-4C3A-9E9A-4C1D2E3F4A5B}
AppName=Branch Merger
AppVersion={#MyVersion}
AppVerName=Branch Merger {#MyVersion}
AppPublisher=eweleylee
DefaultDirName={localappdata}\BranchMerger
DisableProgramGroupPage=yes
DisableDirPage=no
DirExistsWarning=no
Uninstallable=no
PrivilegesRequired=lowest
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputBaseFilename=BranchMerger-Setup
ArchitecturesInstallIn64BitMode=x64compatible

[Messages]
WelcomeLabel2=This will install [name] on your computer.%n%nChoose where to install it on the next screen, then click Install.

[Files]
; Velopack's installer, embedded and extracted to a temp folder at run time (not copied
; into the install dir - Velopack lays out the app itself).
Source: "BranchMerger-win-Setup.exe"; Flags: dontcopy

[Run]
; Offer to launch the app on the finish page (Velopack installs it under \current).
Filename: "{app}\current\BranchMerger.Api.exe"; Description: "Launch Branch Merger"; \
  Flags: postinstall nowait skipifsilent; Check: AppExeExists

[Code]
function AppExeExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\current\BranchMerger.Api.exe'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  SetupPath: string;
begin
  if CurStep = ssInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Installing Branch Merger...';
    ExtractTemporaryFile('BranchMerger-win-Setup.exe');
    SetupPath := ExpandConstant('{tmp}\BranchMerger-win-Setup.exe');
    if not Exec(SetupPath, '--silent --installto "' + ExpandConstant('{app}') + '"',
                '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      MsgBox('Could not start the installer:' + #13#10 + SysErrorMessage(ResultCode),
             mbError, MB_OK)
    else if ResultCode <> 0 then
      MsgBox('Installation reported an error (code ' + IntToStr(ResultCode) + ').',
             mbError, MB_OK);
  end;
end;
