$ErrorActionPreference = 'Stop'

# SoundGeek ships an Inno Setup installer. The package downloads it from the GitHub release for the
# matching tag and verifies it against a SHA-256 checksum rather than embedding the binary. Because
# nothing is embedded, this package must NOT contain a tools\VERIFICATION.txt - that file is only
# for packages that ship a binary inside the nupkg, and including one is what the USP 8.0.0
# submission was rejected for.
$packageArgs = @{
  packageName    = 'soundgeek'
  fileType       = 'exe'
  url            = 'https://github.com/techygeekshome/SoundGeek/releases/download/v1.0.0/SoundGeekSetup.exe'
  checksum       = 'd586166a5e1aea8c169266a9aea124eda9b38721c11664dd373d320ac388a04f'
  checksumType   = 'sha256'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
