Import-Module "C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1" -Force

$Script = 'C:\Support\Clients\Other\DeleteComputers\GetComputers.ps1'
$OutputScript = 'C:\Support\Clients\Other\DeleteComputers\PortableRemoveComputers.ps1'

$Summary = Initialize-PortableScript -FilePath $Script -OutputPath $OutputScript -ApprovedModules 'PSSharedGoods', 'PSEventViewer', 'PSWriteColor'

$Summary.Summary | Format-Table -AutoSize
$Summary.SummaryFiltered | Format-Table -AutoSize