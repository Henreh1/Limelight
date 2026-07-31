#define MyAppName "Limelight"
#define MyAppPublisher "henreh"
#define MyAppExeName "Limelight.exe"
#define MyAppUrl "https://henreh1.github.io/LimelightWiki/"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-early-access"
#endif

#ifndef PublishDir
  #error PublishDir must point to the prepared Limelight publish folder.
#endif

#ifndef OutputDir
  #define OutputDir ".\Output"
#endif

#ifndef WizardImagePath
  #define WizardImagePath ".\Assets\LimelightWizard.bmp"
#endif

#ifndef WizardSmallImagePath
  #define WizardSmallImagePath ".\Assets\LimelightWizardSmall.bmp"
#endif

[Setup]
AppId={{82b1830c-6897-4b62-82ff-898f162fe054}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir={#OutputDir}
OutputBaseFilename=LimelightSetup-{#MyAppVersion}
SetupIconFile=..\Assets\Limelight.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark polar includetitlebar hidebevels
WizardBackColor=#080912
WizardImageBackColor=#131629
WizardSmallImageBackColor=#131629
WizardSizePercent=115
WizardImageFile={#WizardImagePath}
WizardSmallImageFile={#WizardSmallImagePath}
WizardImageStretch=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
Uninstallable=yes
CreateUninstallRegKey=yes
SetupLogging=yes
MinVersion=10.0.17763

#ifdef EnableSigning
SignTool=limelight
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=YOUR MODS. YOUR STAGE.
WelcomeLabel2=Install Limelight Early Access.%n%nLimelight manages Dead as Disco mods, profiles, and optional live switching from one place.
WizardSelectDir=CHOOSE THE INSTALL LOCATION
SelectDirDesc=Choose where Limelight should take the spotlight.
SelectDirLabel3=Limelight installs for this Windows account. Your mod library, profiles, settings, and reports are stored separately and are preserved during upgrades or uninstall.
SelectDirBrowseLabel=Select Next to keep this location, or Browse to choose another folder.
WizardSelectTasks=CHOOSE YOUR SHORTCUTS
SelectTasksDesc=Decide where Limelight should appear on Windows.
SelectTasksLabel2=Choose any extra shortcuts you would like Limelight to create.
WizardReady=FINAL CHECK
ReadyLabel1=READY FOR THE SPOTLIGHT?
ReadyLabel2a=Select Install to set the stage, or Back to review your choices.
ReadyLabel2b=Select Install to set the stage.
WizardInstalling=SETTING THE STAGE
InstallingLabel=Limelight is being installed for this Windows account.
FinishedHeadingLabel=LIMELIGHT IS READY
FinishedLabel=Installation is complete. Limelight can now connect to Dead as Disco and prepare its managed Live Loader when you choose to use it.
FinishedLabelNoIcons=Installation is complete. Limelight is ready to take the spotlight.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Limelight Documentation"; Filename: "{#MyAppUrl}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  LimelightBackground = $00120908;
  LimelightPanel = $00291613;
  LimelightRaisedPanel = $00351E1A;
  LimelightBorder = $004D302C;
  LimelightPink = $00AC3CFF;
  LimelightCyan = $00FFE735;
  LimelightText = $00FFF5F7;
  LimelightMuted = $00AF9692;

var
  ThemedBackButton: TPanel;
  ThemedBackText: TNewStaticText;
  ThemedNextButton: TPanel;
  ThemedNextText: TNewStaticText;
  ThemedCancelButton: TPanel;
  ThemedCancelText: TNewStaticText;

function CleanButtonCaption(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '&', '', True);
end;

procedure RunBackButton(Sender: TObject);
begin
  WizardForm.BackButton.OnClick(WizardForm.BackButton);
end;

procedure RunNextButton(Sender: TObject);
begin
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure RunCancelButton(Sender: TObject);
begin
  WizardForm.CancelButton.OnClick(WizardForm.CancelButton);
end;

procedure PrepareThemedButton(
  ButtonPanel: TPanel;
  ButtonText: TNewStaticText;
  SourceButton: TNewButton;
  BackgroundColor: TColor;
  ClickHandler: TNotifyEvent);
begin
  ButtonPanel.Parent := WizardForm;
  ButtonPanel.SetBounds(
    SourceButton.Left,
    SourceButton.Top,
    SourceButton.Width,
    SourceButton.Height);
  ButtonPanel.BevelOuter := bvNone;
  ButtonPanel.Caption := '';
  ButtonPanel.StyleElements := [];
  ButtonPanel.Color := BackgroundColor;
  ButtonPanel.ParentBackground := False;
  ButtonPanel.Cursor := crHand;
  ButtonPanel.OnClick := ClickHandler;

  ButtonText.Parent := ButtonPanel;
  ButtonText.AutoSize := False;
  ButtonText.SetBounds(0, ScaleY(6), ButtonPanel.Width, ScaleY(17));
  ButtonText.Alignment := taCenter;
  ButtonText.Caption := CleanButtonCaption(SourceButton.Caption);
  ButtonText.StyleElements := [];
  ButtonText.Font.Name := 'Bahnschrift';
  ButtonText.Font.Size := 9;
  ButtonText.Font.Style := [fsBold];
  ButtonText.Font.Color := LimelightText;
  ButtonText.Cursor := crHand;
  ButtonText.OnClick := ClickHandler;
end;

procedure UpdateThemedButtons;
begin
  ThemedBackButton.Visible := WizardForm.BackButton.Visible;
  ThemedBackButton.Enabled := WizardForm.BackButton.Enabled;
  ThemedBackText.Enabled := WizardForm.BackButton.Enabled;
  ThemedBackText.Caption := CleanButtonCaption(WizardForm.BackButton.Caption);

  ThemedNextButton.Visible := WizardForm.NextButton.Visible;
  ThemedNextButton.Enabled := WizardForm.NextButton.Enabled;
  ThemedNextText.Enabled := WizardForm.NextButton.Enabled;
  ThemedNextText.Caption := CleanButtonCaption(WizardForm.NextButton.Caption);

  ThemedCancelButton.Visible := WizardForm.CancelButton.Visible;
  ThemedCancelButton.Enabled := WizardForm.CancelButton.Enabled;
  ThemedCancelText.Enabled := WizardForm.CancelButton.Enabled;
  ThemedCancelText.Caption := CleanButtonCaption(WizardForm.CancelButton.Caption);

  if WizardForm.BackButton.Enabled then
    ThemedBackText.Font.Color := LimelightText
  else
    ThemedBackText.Font.Color := LimelightMuted;

  if WizardForm.NextButton.Enabled then
    ThemedNextText.Font.Color := LimelightText
  else
    ThemedNextText.Font.Color := LimelightMuted;

  if WizardForm.CancelButton.Enabled then
    ThemedCancelText.Font.Color := LimelightText
  else
    ThemedCancelText.Font.Color := LimelightMuted;

  ThemedBackButton.BringToFront;
  ThemedNextButton.BringToFront;
  ThemedCancelButton.BringToFront;
end;

procedure ThemeTextLabel(LabelControl: TNewStaticText; Accent: Boolean);
begin
  LabelControl.Font.Name := 'Bahnschrift';
  if Accent then
    LabelControl.Font.Color := LimelightCyan
  else
    LabelControl.Font.Color := LimelightText;
end;

function CreatePagePanel(
  ParentControl: TWinControl;
  LeftValue: Integer;
  TopValue: Integer;
  WidthValue: Integer;
  HeightValue: Integer;
  BackgroundColor: TColor): TPanel;
begin
  Result := TPanel.Create(WizardForm);
  Result.Parent := ParentControl;
  Result.SetBounds(LeftValue, TopValue, WidthValue, HeightValue);
  Result.Caption := '';
  Result.BevelOuter := bvNone;
  Result.StyleElements := [];
  Result.Color := BackgroundColor;
  Result.ParentBackground := False;
end;

function CreatePageLabel(
  ParentControl: TWinControl;
  LeftValue: Integer;
  TopValue: Integer;
  WidthValue: Integer;
  HeightValue: Integer;
  CaptionText: String;
  FontSize: Integer;
  TextColor: TColor;
  BoldText: Boolean): TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  Result.Parent := ParentControl;
  Result.AutoSize := False;
  Result.SetBounds(LeftValue, TopValue, WidthValue, HeightValue);
  Result.Caption := CaptionText;
  Result.Font.Name := 'Bahnschrift';
  Result.Font.Size := FontSize;
  Result.Font.Color := TextColor;
  Result.WordWrap := True;
  if BoldText then
    Result.Font.Style := [fsBold]
  else
    Result.Font.Style := [];
end;

procedure CreateBrandedWelcomePage;
var
  HeroPanel: TPanel;
  AccentPanel: TPanel;
  StatusPanel: TPanel;
  LogoPanel: TPanel;
  LogoImage: TBitmapImage;
  PageWidth: Integer;
begin
  WizardForm.WizardBitmapImage.Visible := False;
  WizardForm.WizardBitmapImage2.Visible := False;
  WizardForm.WelcomeLabel1.Visible := False;
  WizardForm.WelcomeLabel2.Visible := False;
  WizardForm.WelcomePage.Color := LimelightBackground;

  PageWidth := WizardForm.WelcomePage.ClientWidth;
  HeroPanel := CreatePagePanel(
    WizardForm.WelcomePage,
    ScaleX(24),
    ScaleY(24),
    PageWidth - ScaleX(48),
    ScaleY(286),
    LimelightPanel);

  AccentPanel := CreatePagePanel(
    HeroPanel,
    0,
    0,
    ScaleX(5),
    HeroPanel.Height,
    LimelightPink);

  CreatePageLabel(
    HeroPanel,
    ScaleX(30),
    ScaleY(28),
    HeroPanel.Width - ScaleX(205),
    ScaleY(20),
    'LIMELIGHT EARLY ACCESS',
    9,
    LimelightCyan,
    True);
  CreatePageLabel(
    HeroPanel,
    ScaleX(30),
    ScaleY(55),
    HeroPanel.Width - ScaleX(205),
    ScaleY(44),
    'YOUR MODS. YOUR STAGE.',
    20,
    LimelightText,
    True);
  CreatePageLabel(
    HeroPanel,
    ScaleX(30),
    ScaleY(112),
    HeroPanel.Width - ScaleX(205),
    ScaleY(68),
    'Install Limelight for this Windows account and manage Dead as Disco mods, profiles, and optional live switching from one place.',
    10,
    LimelightMuted,
    False);

  LogoPanel := CreatePagePanel(
    HeroPanel,
    HeroPanel.Width - ScaleX(150),
    ScaleY(38),
    ScaleX(112),
    ScaleY(112),
    LimelightBackground);
  LogoImage := TBitmapImage.Create(WizardForm);
  LogoImage.Parent := LogoPanel;
  LogoImage.Bitmap := WizardForm.WizardSmallBitmapImage.Bitmap;
  LogoImage.BackColor := LimelightBackground;
  LogoImage.Stretch := True;
  LogoImage.SetBounds(ScaleX(13), ScaleY(10), ScaleX(86), ScaleY(91));

  StatusPanel := CreatePagePanel(
    HeroPanel,
    ScaleX(30),
    ScaleY(205),
    HeroPanel.Width - ScaleX(60),
    ScaleY(48),
    LimelightRaisedPanel);
  CreatePageLabel(
    StatusPanel,
    ScaleX(18),
    ScaleY(10),
    StatusPanel.Width - ScaleX(36),
    ScaleY(28),
    'EARLY ACCESS   |   MANAGED LIVE LOADER   |   PROFILES PRESERVED',
    8,
    LimelightCyan,
    True);

  CreatePageLabel(
    WizardForm.WelcomePage,
    ScaleX(28),
    ScaleY(328),
    PageWidth - ScaleX(56),
    ScaleY(40),
    'NEXT  /  CHOOSE WHERE LIMELIGHT SHOULD BE INSTALLED',
    8,
    LimelightPink,
    True);
end;

procedure CreateBrandedTasksPage;
var
  ShortcutPanel: TPanel;
  AccentPanel: TPanel;
  NotePanel: TPanel;
  PageWidth: Integer;
begin
  WizardForm.SelectTasksLabel.Visible := False;
  WizardForm.SelectTasksPage.Color := LimelightBackground;

  PageWidth := WizardForm.SelectTasksPage.ClientWidth;
  ShortcutPanel := CreatePagePanel(
    WizardForm.SelectTasksPage,
    ScaleX(24),
    ScaleY(26),
    PageWidth - ScaleX(48),
    ScaleY(140),
    LimelightPanel);

  AccentPanel := CreatePagePanel(
    ShortcutPanel,
    0,
    0,
    ScaleX(5),
    ShortcutPanel.Height,
    LimelightPink);

  CreatePageLabel(
    ShortcutPanel,
    ScaleX(26),
    ScaleY(22),
    ShortcutPanel.Width - ScaleX(52),
    ScaleY(20),
    'DESKTOP SHORTCUT',
    9,
    LimelightCyan,
    True);
  CreatePageLabel(
    ShortcutPanel,
    ScaleX(26),
    ScaleY(48),
    ShortcutPanel.Width - ScaleX(52),
    ScaleY(22),
    'KEEP LIMELIGHT CLOSE',
    14,
    LimelightText,
    True);
  CreatePageLabel(
    ShortcutPanel,
    ScaleX(26),
    ScaleY(76),
    ShortcutPanel.Width - ScaleX(52),
    ScaleY(30),
    'Add a shortcut to your desktop for quick access. You can leave this off and use the Start menu instead.',
    9,
    LimelightMuted,
    False);

  // I keep Inno Setup's real checkbox so keyboard and accessibility behaviour remain reliable.
  WizardForm.TasksList.Parent := ShortcutPanel;
  WizardForm.TasksList.SetBounds(
    ScaleX(22),
    ScaleY(104),
    ShortcutPanel.Width - ScaleX(44),
    ScaleY(29));
  WizardForm.TasksList.StyleElements := [];
  WizardForm.TasksList.Color := LimelightRaisedPanel;
  WizardForm.TasksList.Font.Name := 'Bahnschrift';
  WizardForm.TasksList.Font.Color := LimelightText;
  WizardForm.TasksList.BorderStyle := bsNone;

  NotePanel := CreatePagePanel(
    WizardForm.SelectTasksPage,
    ScaleX(24),
    ScaleY(180),
    PageWidth - ScaleX(48),
    ScaleY(70),
    LimelightRaisedPanel);
  CreatePageLabel(
    NotePanel,
    ScaleX(20),
    ScaleY(12),
    NotePanel.Width - ScaleX(40),
    ScaleY(18),
    'A CLEAN WINDOWS INSTALL',
    8,
    LimelightPink,
    True);
  CreatePageLabel(
    NotePanel,
    ScaleX(20),
    ScaleY(34),
    NotePanel.Width - ScaleX(40),
    ScaleY(27),
    'This only adds a shortcut. Limelight will not install a background service or start with Windows.',
    9,
    LimelightMuted,
    False);
end;

procedure CreateBrandedFinishedPage;
var
  HeroPanel: TPanel;
  AccentPanel: TPanel;
  StatusPanel: TPanel;
  FirstStepPanel: TPanel;
  SecondStepPanel: TPanel;
  LaunchPanel: TPanel;
  LogoPanel: TPanel;
  LogoImage: TBitmapImage;
  PageWidth: Integer;
  StepWidth: Integer;
  ContentWidth: Integer;
begin
  WizardForm.FinishedHeadingLabel.Visible := False;
  WizardForm.FinishedLabel.Visible := False;
  WizardForm.FinishedPage.Color := LimelightBackground;

  PageWidth := WizardForm.FinishedPage.ClientWidth;
  ContentWidth := PageWidth - ScaleX(48);
  HeroPanel := CreatePagePanel(
    WizardForm.FinishedPage,
    ScaleX(24),
    ScaleY(12),
    ContentWidth,
    ScaleY(154),
    LimelightPanel);

  AccentPanel := CreatePagePanel(
    HeroPanel,
    0,
    0,
    ScaleX(5),
    HeroPanel.Height,
    LimelightCyan);

  CreatePageLabel(
    HeroPanel,
    ScaleX(24),
    ScaleY(16),
    HeroPanel.Width - ScaleX(170),
    ScaleY(18),
    'INSTALLATION COMPLETE',
    9,
    LimelightCyan,
    True);
  CreatePageLabel(
    HeroPanel,
    ScaleX(24),
    ScaleY(39),
    HeroPanel.Width - ScaleX(170),
    ScaleY(34),
    'LIMELIGHT IS READY',
    17,
    LimelightText,
    True);
  CreatePageLabel(
    HeroPanel,
    ScaleX(24),
    ScaleY(78),
    HeroPanel.Width - ScaleX(170),
    ScaleY(42),
    'The manager is installed and ready. Its managed Live Loader will only be prepared when you choose to use it.',
    9,
    LimelightMuted,
    False);

  LogoPanel := CreatePagePanel(
    HeroPanel,
    HeroPanel.Width - ScaleX(126),
    ScaleY(21),
    ScaleX(94),
    ScaleY(94),
    LimelightBackground);
  LogoImage := TBitmapImage.Create(WizardForm);
  LogoImage.Parent := LogoPanel;
  LogoImage.Bitmap := WizardForm.WizardSmallBitmapImage.Bitmap;
  LogoImage.BackColor := LimelightBackground;
  LogoImage.Stretch := True;
  LogoImage.SetBounds(ScaleX(11), ScaleY(9), ScaleX(72), ScaleY(76));

  StatusPanel := CreatePagePanel(
    HeroPanel,
    ScaleX(24),
    ScaleY(122),
    HeroPanel.Width - ScaleX(48),
    ScaleY(24),
    LimelightRaisedPanel);
  CreatePageLabel(
    StatusPanel,
    ScaleX(14),
    ScaleY(3),
    StatusPanel.Width - ScaleX(36),
    ScaleY(18),
    'READY TO TAKE THE SPOTLIGHT',
    8,
    LimelightCyan,
    True);

  StepWidth := (ContentWidth - ScaleX(12)) div 2;
  FirstStepPanel := CreatePagePanel(
    WizardForm.FinishedPage,
    ScaleX(24),
    ScaleY(178),
    StepWidth,
    ScaleY(62),
    LimelightRaisedPanel);
  CreatePageLabel(
    FirstStepPanel,
    ScaleX(18),
    ScaleY(10),
    FirstStepPanel.Width - ScaleX(36),
    ScaleY(18),
    '01  OPEN LIMELIGHT',
    8,
    LimelightPink,
    True);
  CreatePageLabel(
    FirstStepPanel,
    ScaleX(18),
    ScaleY(31),
    FirstStepPanel.Width - ScaleX(36),
    ScaleY(25),
    'Start the manager and connect your game folder.',
    9,
    LimelightMuted,
    False);

  SecondStepPanel := CreatePagePanel(
    WizardForm.FinishedPage,
    ScaleX(36) + StepWidth,
    ScaleY(178),
    StepWidth,
    ScaleY(62),
    LimelightRaisedPanel);
  CreatePageLabel(
    SecondStepPanel,
    ScaleX(18),
    ScaleY(10),
    SecondStepPanel.Width - ScaleX(36),
    ScaleY(18),
    '02  CHOOSE YOUR HEADLINER',
    8,
    LimelightCyan,
    True);
  CreatePageLabel(
    SecondStepPanel,
    ScaleX(18),
    ScaleY(31),
    SecondStepPanel.Width - ScaleX(36),
    ScaleY(25),
    'Import or drag in a mod archive from the Limelight dashboard.',
    9,
    LimelightMuted,
    False);

  LaunchPanel := CreatePagePanel(
    WizardForm.FinishedPage,
    ScaleX(24),
    ScaleY(252),
    ContentWidth,
    ScaleY(48),
    LimelightPanel);
  WizardForm.RunList.SetBounds(
    ScaleX(16),
    ScaleY(8),
    LaunchPanel.Width - ScaleX(32),
    ScaleY(31));
  WizardForm.RunList.Parent := LaunchPanel;
  WizardForm.RunList.StyleElements := [];
  WizardForm.RunList.Color := LimelightPanel;
  WizardForm.RunList.Font.Name := 'Bahnschrift';
  WizardForm.RunList.Font.Color := LimelightText;
  WizardForm.RunList.BorderStyle := bsNone;

  CreatePageLabel(
    WizardForm.FinishedPage,
    ScaleX(24),
    ScaleY(312),
    ContentWidth,
    ScaleY(18),
    'FINISH  /  OPEN LIMELIGHT AND TAKE THE SPOTLIGHT',
    8,
    LimelightPink,
    True);
end;

procedure InitializeWizard;
begin
  // I keep the native installer controls, but dress every visible page in Limelight's palette.
  WizardForm.Caption := 'Limelight  |  Early Access Installer';
  WizardForm.Color := LimelightBackground;
  WizardForm.MainPanel.Color := LimelightBackground;
  WizardForm.InnerPage.Color := LimelightBackground;
  ThemeTextLabel(WizardForm.PageNameLabel, True);
  ThemeTextLabel(WizardForm.PageDescriptionLabel, False);
  ThemeTextLabel(WizardForm.WelcomeLabel1, False);
  ThemeTextLabel(WizardForm.WelcomeLabel2, False);
  ThemeTextLabel(WizardForm.FinishedHeadingLabel, False);
  ThemeTextLabel(WizardForm.FinishedLabel, False);
  ThemeTextLabel(WizardForm.SelectDirLabel, False);
  ThemeTextLabel(WizardForm.SelectStartMenuFolderLabel, False);
  ThemeTextLabel(WizardForm.SelectTasksLabel, False);
  ThemeTextLabel(WizardForm.ReadyLabel, True);
  ThemeTextLabel(WizardForm.PreparingLabel, True);
  ThemeTextLabel(WizardForm.StatusLabel, False);

  WizardForm.WelcomeLabel1.Font.Size := 18;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.PageNameLabel.Font.Style := [fsBold];
  WizardForm.FinishedHeadingLabel.Font.Size := 16;
  WizardForm.FinishedHeadingLabel.Font.Style := [fsBold];

  WizardForm.DirEdit.Color := LimelightPanel;
  WizardForm.DirEdit.Font.Color := LimelightText;
  WizardForm.GroupEdit.Color := LimelightPanel;
  WizardForm.GroupEdit.Font.Color := LimelightText;
  WizardForm.ReadyMemo.Color := LimelightPanel;
  WizardForm.ReadyMemo.Font.Color := LimelightText;
  WizardForm.InfoBeforeMemo.Color := LimelightPanel;
  WizardForm.InfoBeforeMemo.Font.Color := LimelightText;
  WizardForm.InfoAfterMemo.Color := LimelightPanel;
  WizardForm.InfoAfterMemo.Font.Color := LimelightText;

  WizardForm.TasksList.Color := LimelightPanel;
  WizardForm.TasksList.Font.Name := 'Bahnschrift';
  WizardForm.TasksList.Font.Color := LimelightText;
  WizardForm.TasksList.BorderStyle := bsSingle;
  WizardForm.DiskSpaceLabel.Font.Color := LimelightMuted;
  WizardForm.NoIconsCheck.Font.Color := LimelightText;
  WizardForm.RunList.Color := LimelightPanel;
  WizardForm.RunList.Font.Color := LimelightText;

  CreateBrandedWelcomePage;
  CreateBrandedTasksPage;
  CreateBrandedFinishedPage;

  WizardForm.NextButton.Font.Name := 'Bahnschrift';
  WizardForm.NextButton.Font.Style := [fsBold];
  WizardForm.BackButton.Font.Name := 'Bahnschrift';
  WizardForm.CancelButton.Font.Name := 'Bahnschrift';

  // I place themed controls over the reliable native navigation buttons.
  // Keyboard behaviour stays native while the visible controls match Limelight.
  ThemedBackButton := TPanel.Create(WizardForm);
  ThemedBackText := TNewStaticText.Create(WizardForm);
  PrepareThemedButton(
    ThemedBackButton,
    ThemedBackText,
    WizardForm.BackButton,
    LimelightRaisedPanel,
    @RunBackButton);

  ThemedNextButton := TPanel.Create(WizardForm);
  ThemedNextText := TNewStaticText.Create(WizardForm);
  PrepareThemedButton(
    ThemedNextButton,
    ThemedNextText,
    WizardForm.NextButton,
    LimelightPink,
    @RunNextButton);

  ThemedCancelButton := TPanel.Create(WizardForm);
  ThemedCancelText := TNewStaticText.Create(WizardForm);
  PrepareThemedButton(
    ThemedCancelButton,
    ThemedCancelText,
    WizardForm.CancelButton,
    LimelightRaisedPanel,
    @RunCancelButton);

  UpdateThemedButtons;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  UpdateThemedButtons;
end;
