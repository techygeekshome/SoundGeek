$ErrorActionPreference = 'Stop'

# SoundGeek ships an Inno Setup installer. The package downloads it from the GitHub release for the
# matching tag and verifies it against a SHA-256 checksum rather than embedding the binary. Because
# nothing is embedded, this package must NOT contain a tools\VERIFICATION.txt - that file is only
# for packages that ship a binary inside the nupkg, and including one is what the USP 8.0.0
# submission was rejected for.
$packageArgs = @{
  packageName    = 'soundgeek'
  fileType       = 'exe'
  url            = 'https://github.com/techygeekshome/SoundGeek/releases/download/v1.0.1/SoundGeekSetup.exe'
  checksum       = 'd7ab797df5eb592100ad753aacdddfc355f287a1feb8815f5d56e03a9a1e4d6c'
  checksumType   = 'sha256'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
