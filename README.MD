# PSPublishModule - PowerShell Module

<p align="center">
  <a href="https://dev.azure.com/evotecpl/PSPublishModule/_build/latest?definitionId=3"><img src="https://img.shields.io/azure-devops/build/evotecpl/a5ce5d3e-9ae2-49a0-9905-53eb41205fc9/7?label=Azure%20Pipelines&style=flat-square"></a>
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/v/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/vpre/PSPublishModule.svg?label=powershell%20gallery%20preview&colorB=yellow&style=flat-square"></a>
  <a href="https://github.com/EvotecIT/PSPublishModule"><img src="https://img.shields.io/github/license/EvotecIT/PSPublishModule.svg?style=flat-square"></a>

</p>

<p align="center">
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/p/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://github.com/EvotecIT/PSPublishModule"><img src="https://img.shields.io/github/languages/top/evotecit/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://github.com/EvotecIT/PSPublishModule"><img src="https://img.shields.io/github/languages/code-size/evotecit/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/dt/PSPublishModule.svg?style=flat-square"></a>
</p>

<p align="center">
  <a href="https://twitter.com/PrzemyslawKlys"><img src="https://img.shields.io/twitter/follow/PrzemyslawKlys.svg?label=Twitter%20%40PrzemyslawKlys&style=flat-square"></a>
  <a href="https://evotec.xyz/hub"><img src="https://img.shields.io/badge/Blog-evotec.xyz-2A6496.svg?style=flat-square"></a>
  <a href="https://www.linkedin.com/in/pklys"><img src="https://img.shields.io/badge/LinkedIn-pklys-0077B5.svg?logo=LinkedIn&style=flat-square"></a>
</p>

## Description

This module is a module builder helper that helps build PowerShell modules "Evotec way".
It allows us to make sure our modules are built the same way every time making the process really easy to build and publish new versions.

Each module that is published to PowerShellGallery has Publish folder next to it and within it there is PS1 script which executes module building that uses this module builder.

## Support This Project

If you find this project helpful, please consider supporting its development.
Your sponsorship will help the maintainers dedicate more time to maintenance and new feature development for everyone.

It takes a lot of time and effort to create and maintain this project.
By becoming a sponsor, you can help ensure that it stays free and accessible to everyone who needs it.

To become a sponsor, you can choose from the following options:

 - [Become a sponsor via GitHub Sponsors :heart:](https://github.com/sponsors/PrzemyslawKlys)
 - [Become a sponsor via PayPal :heart:](https://paypal.me/PrzemyslawKlys)
 -
Your sponsorship is completely optional and not required for using this project.
We want this project to remain open-source and available for anyone to use for free,
regardless of whether they choose to sponsor it or not.

If you work for a company that uses our .NET libraries or PowerShell Modules,
please consider asking your manager or marketing team if your company would be interested in supporting this project.
Your company's support can help us continue to maintain and improve this project for the benefit of everyone.

Thank you for considering supporting this project!

### Installing

```powershell
Install-Module -Name PSPublishModule -AllowClobber -Force
```

Force and AllowClobber aren't necessary but they do skip errors in case some appear.

### Updating

```powershell
Update-Module -Name PSPublishModule
```

That's it. Whenever there's a new version you simply run the command and you can enjoy it. Remember, that you may need to close, reopen the PowerShell session if you have already used the module before updating it.

**The important thing** is if something works for you on production, keep using it till you test the new version on a test computer. I do changes that may not be big, but big enough that auto-update will break your code. For example, small rename to a parameter and your code stops working! Be responsible!