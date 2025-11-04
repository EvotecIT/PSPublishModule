<#
$FileName = "C:\Support\Important\Certificates\Przemyslaw Klys EVOTEC.pfx"
$Password = ']LVivNG8T@hpe976TqNdH#V3t,ax6v)@wor2Tb:&w.vfK]=LMK#rg%jXao^KR=Z)'
[System.Security.Cryptography.X509Certificates.X509Certificate2Collection] $coll = [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
$coll.Import($filename, $password);
[byte[]] $nopw = $coll.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx);

foreach ($cert in $coll)
{
    $cert.Dispose();
}

$nopw.
#>

$NewPwd = ConvertTo-SecureString -String "]LVivNG8T@hpe976TqNdH#V3t,ax6v)@wor2Tb:&w.vfK]=LMK#rg%jXao^KR=Z)" -Force -AsPlainText

$mypfx = Get-PfxData -FilePath "C:\Support\Important\Certificates\Przemyslaw Klys EVOTEC.pfx" -Password $NewPwd

Export-PfxCertificate -PFXData $mypfx -FilePath C:\mypfx2.pfx -Force