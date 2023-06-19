Import-Module "$PSSCriptRoot\..\PSPublishModule.psd1" -Force

# Step 01 - An initial module structure, manifest, and configuration file will be created
Build-Module -ModuleName 'MyGreatModule' -Path "C:\Support\GitHub"