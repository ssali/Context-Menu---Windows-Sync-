[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=I
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=.\Release\MezeoFile_32.exe
FriendlyName=Mezeo Windows Sync
AppLaunched=cmd.exe /c setup.cmd
PostInstallCmd=cmd.exe /c setup_cleanup.cmd
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="MezeoFile_32.msi"
FILE1="setup.exe"
FILE2="setup.cmd"
FILE3="MezeoPostInstallLauncher.exe"
[SourceFiles]
SourceFiles0=.\Release\
SourceFiles1=.\
[SourceFiles0]
%FILE0%=
%FILE1%=
[SourceFiles1]
%FILE2%=
%FILE3%=