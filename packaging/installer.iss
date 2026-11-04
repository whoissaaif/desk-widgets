; Desk Widgets - Inno Setup installer script
; Build:  "C:\Users\saifa\AppData\Local\Programs\Inno Setup 6\ISCC.exe" packaging\installer.iss
; Output: bin\DeskWidgets-Setup.exe

#define MyAppName "Desk Widgets"
#define MyAppVersion "1.8.1"
#define MyAppPublisher "Desk Widgets"
#define MyAppURL "https://whoissaaif.github.io/desk-widgets/"
#define MyAppExeName "DeskWidgets.exe"

[Setup]
AppId={{E8A3B2F1-7C4D-4E5A-9F6B-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=license.rtf
OutputDir=..\bin
OutputBaseFilename=DeskWidgets-Setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardImageFile=assets\WizardImage.bmp
WizardSmallImageFile=assets\WizardSmallImage.bmp
WizardSizePercent=120,120
WizardImageStretch=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Desk Widgets.
WelcomeLabel2=Lightweight desktop widgets for Windows.
FinishedHeadingLabel=You're all set.
FinishedLabel=Desk Widgets is ready to use.
ClickFinish=Click Finish to launch.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked
Name: "startwithwindows"; Description: "Start with Windows"; GroupDescription: "Options:"

[Files]
Source: "..\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\icon-96.bmp"; Flags: dontcopy

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; URL protocol: deskwidgets://
Root: HKCU; Subkey: "Software\Classes\deskwidgets"; ValueType: string; ValueData: "URL:Desk Widgets"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\deskwidgets"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\deskwidgets\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"
Root: HKCU; Subkey: "Software\Classes\deskwidgets\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
; Autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DeskWidgets"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" --background"; Tasks: startwithwindows; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\DeskWidgets"
Type: filesandordirs; Name: "{localappdata}\DeskWidgets\previews"

[Code]
var
  ResultCode: Integer;
  IconBmp: TBitmap;
  pageW, pageH, iconX, iconY, textW, textX, textY: Integer;
  R: TRect;

function GetWindowLong(hWnd: HWND; nIndex: Integer): Longint;
  external 'GetWindowLongW@user32.dll stdcall';
function SetWindowLong(hWnd: HWND; nIndex: Integer; dwNewLong: Longint): Longint;
  external 'SetWindowLongW@user32.dll stdcall';
function SetWindowPos(hWnd: HWND; hWndInsertAfter: HWND; X, Y, cx, cy: Integer; uFlags: Cardinal): Boolean;
  external 'SetWindowPos@user32.dll stdcall';
function SetWindowTheme(hWnd: HWND; pszSubAppName: String; pszSubIdList: String): Integer;
  external 'SetWindowTheme@uxtheme.dll stdcall';
function BitBlt(hdcDest: THandle; nXDest, nYDest, nWidth, nHeight: Integer; hdcSrc: THandle; nXSrc, nYSrc: Integer; dwRop: Cardinal): Boolean;
  external 'BitBlt@gdi32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    Exec('taskkill.exe', '/F /IM DeskWidgets.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure InitializeWizard();
var
  ExStyle: Longint;
begin
  WizardForm.Color := $FFFFFF;
  WizardForm.Font.Color := $1F1D1D;
  WizardForm.Font.Name := 'Segoe UI Variable Text';

  WizardForm.MainPanel.Color := $FFFFFF;
  WizardForm.InnerPage.Color := $FFFFFF;
  WizardForm.WelcomePage.Color := $F7F5F5;
  WizardForm.FinishedPage.Color := $F7F5F5;
  WizardForm.LicensePage.Color := $FFFFFF;
  WizardForm.SelectDirPage.Color := $FFFFFF;
  WizardForm.SelectTasksPage.Color := $FFFFFF;
  WizardForm.InstallingPage.Color := $FFFFFF;
  WizardForm.ReadyPage.Color := $FFFFFF;

  // Header
  WizardForm.PageNameLabel.Font.Color := $1F1D1D;
  WizardForm.PageNameLabel.Font.Name := 'Segoe UI Variable Display';
  WizardForm.PageNameLabel.Font.Style := [fsBold];
  WizardForm.PageDescriptionLabel.Font.Color := $8B8686;
  // Nudge header labels down to prevent top clipping
  WizardForm.PageNameLabel.Top := WizardForm.PageNameLabel.Top + 4;
  WizardForm.PageDescriptionLabel.Top := WizardForm.PageDescriptionLabel.Top + 4;

  // Welcome + finished pages: full-page centered layout drawn at runtime
  WizardForm.WelcomeLabel1.Visible := False;
  WizardForm.WelcomeLabel2.Visible := False;
  WizardForm.FinishedHeadingLabel.Visible := False;
  WizardForm.FinishedLabel.Visible := False;

  ExtractTemporaryFile('icon-96.bmp');
  IconBmp := TBitmap.Create;
  IconBmp.LoadFromFile(ExpandConstant('{tmp}\icon-96.bmp'));

  // Welcome page
  pageW := WizardForm.WizardBitmapImage.Parent.Width;
  pageH := WizardForm.WizardBitmapImage.Parent.Height;
  WizardForm.WizardBitmapImage.Stretch := False;
  WizardForm.WizardBitmapImage.SetBounds(0, 0, pageW, pageH);
  WizardForm.WizardBitmapImage.Bitmap.Width := pageW;
  WizardForm.WizardBitmapImage.Bitmap.Height := pageH;
  WizardForm.WizardBitmapImage.Bitmap.Canvas.Brush.Color := $F7F5F5;
  R.Left := 0; R.Top := 0; R.Right := pageW; R.Bottom := pageH;
  WizardForm.WizardBitmapImage.Bitmap.Canvas.FillRect(R);
  iconX := (pageW - 96) div 2;
  iconY := (pageH div 2) - 80;
  BitBlt(WizardForm.WizardBitmapImage.Bitmap.Canvas.Handle, iconX, iconY, 96, 96, IconBmp.Canvas.Handle, 0, 0, $00CC0020);
  WizardForm.WizardBitmapImage.Bitmap.Canvas.Brush.Style := bsClear;
  WizardForm.WizardBitmapImage.Bitmap.Canvas.Font.Name := 'Segoe UI Variable Display';
  WizardForm.WizardBitmapImage.Bitmap.Canvas.Font.Size := 22;
  WizardForm.WizardBitmapImage.Bitmap.Canvas.Font.Style := [fsBold];
  WizardForm.WizardBitmapImage.Bitmap.Canvas.Font.Color := $1F1D1D;
  textW := WizardForm.WizardBitmapImage.Bitmap.Canvas.TextWidth('Desk Widgets');
  textX := (pageW - textW) div 2;
  textY := iconY + 96 + 16;
  WizardForm.WizardBitmapImage.Bitmap.Canvas.TextOut(textX, textY, 'Desk Widgets');

  // Finished page
  pageW := WizardForm.WizardBitmapImage2.Parent.Width;
  pageH := WizardForm.WizardBitmapImage2.Parent.Height;
  WizardForm.WizardBitmapImage2.Stretch := False;
  WizardForm.WizardBitmapImage2.SetBounds(0, 0, pageW, pageH);
  WizardForm.WizardBitmapImage2.Bitmap.Width := pageW;
  WizardForm.WizardBitmapImage2.Bitmap.Height := pageH;
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.Brush.Color := $F7F5F5;
  R.Left := 0; R.Top := 0; R.Right := pageW; R.Bottom := pageH;
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.FillRect(R);
  iconX := (pageW - 96) div 2;
  iconY := (pageH div 2) - 96;
  BitBlt(WizardForm.WizardBitmapImage2.Bitmap.Canvas.Handle, iconX, iconY, 96, 96, IconBmp.Canvas.Handle, 0, 0, $00CC0020);
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.Brush.Style := bsClear;
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.Font.Name := 'Segoe UI Variable Display';
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.Font.Size := 22;
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.Font.Style := [fsBold];
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.Font.Color := $1F1D1D;
  textW := WizardForm.WizardBitmapImage2.Bitmap.Canvas.TextWidth('You''re all set.');
  textX := (pageW - textW) div 2;
  textY := iconY + 96 + 16;
  WizardForm.WizardBitmapImage2.Bitmap.Canvas.TextOut(textX, textY, 'You''re all set.');

  IconBmp.Free;

  // Directory edit
  WizardForm.DirEdit.Color := $F7F5F5;
  WizardForm.DirEdit.Font.Color := $1F1D1D;
  WizardForm.SelectDirBrowseLabel.Font.Color := $736E6E;
  WizardForm.SelectDirLabel.Font.Color := $736E6E;
  WizardForm.DiskSpaceLabel.Font.Color := $8B8686;

  // Tasks
  WizardForm.TasksList.Color := $FFFFFF;
  WizardForm.TasksList.Font.Color := $1F1D1D;
  WizardForm.SelectTasksLabel.Font.Color := $736E6E;

  // License
  WizardForm.LicenseMemo.Color := $F7F5F5;
  WizardForm.LicenseMemo.Font.Color := $1F1D1D;
  WizardForm.LicenseMemo.Font.Name := 'Cascadia Mono';
  WizardForm.LicenseMemo.Font.Size := 9;
  WizardForm.LicenseAcceptedRadio.Font.Color := $1F1D1D;
  WizardForm.LicenseNotAcceptedRadio.Font.Color := $1F1D1D;
  WizardForm.LicenseLabel1.Font.Color := $736E6E;

  // Ready to Install memo
  WizardForm.ReadyMemo.Color := $F7F5F5;
  WizardForm.ReadyMemo.Font.Color := $736E6E;
  WizardForm.ReadyLabel.Font.Color := $736E6E;

  // Installing
  WizardForm.StatusLabel.Font.Color := $736E6E;
  WizardForm.FilenameLabel.Font.Color := $8B8686;

  // Run list (launch checkbox on finish page — on top of full-page image)
  WizardForm.RunList.BringToFront;
  WizardForm.RunList.Color := $F7F5F5;
  WizardForm.RunList.Font.Color := $1F1D1D;
  WizardForm.RunList.Height := ScaleY(24);
  WizardForm.RunList.Width := ScaleX(200);
  WizardForm.RunList.Left := (pageW - WizardForm.RunList.Width) div 2;
  WizardForm.RunList.Top := textY + ScaleY(30);

  // Bevel lines
  WizardForm.Bevel.Visible := False;
  WizardForm.Bevel1.Visible := False;

  // Remove white 3D border from memo controls
  ExStyle := GetWindowLong(WizardForm.LicenseMemo.Handle, -20);
  SetWindowLong(WizardForm.LicenseMemo.Handle, -20, ExStyle and (not $00000200));
  SetWindowPos(WizardForm.LicenseMemo.Handle, 0, 0, 0, 0, 0, $0027);

  ExStyle := GetWindowLong(WizardForm.ReadyMemo.Handle, -20);
  SetWindowLong(WizardForm.ReadyMemo.Handle, -20, ExStyle and (not $00000200));
  SetWindowPos(WizardForm.ReadyMemo.Handle, 0, 0, 0, 0, 0, $0027);

  // Light scrollbars
  SetWindowTheme(WizardForm.LicenseMemo.Handle, 'Explorer', '');
  SetWindowTheme(WizardForm.ReadyMemo.Handle, 'Explorer', '');
end;
