Import-Module "$PSSCriptRoot\..\PSPublishModule.psd1" -Force

Build-Module -ModuleName 'MyGreatModule' -Path "C:\Support\GitHub"