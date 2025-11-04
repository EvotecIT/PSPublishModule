


#Get-VTAPIKeyInfo -APIKey $VTApi
#Get-VTFileReport -APIKey $VTApi - #"C:\Support\GitHub\PSPublishModule\Releases\v0.9.47\PSPublishModule.psm1"
#Submit-VTFile -APIKey $VTApi -File "C:\Support\GitHub\PSPublishModule\Releases\v0.9.47\PSPublishModule.psm1"


$API = $VTApi
$File = "C:\Support\GitHub\PSPublishModule\Releases\v0.9.47.zip"
$Item = Get-Item -LiteralPath $File
#$Item
$OutputVirusTotal = Invoke-WebRequest -Method "POST" -Uri "https://www.virustotal.com/vtapi/v2/file/scan" -Body @{ apikey = $api; file = $Item }
$OutputJSON = $OutputVirusTotal.Content | ConvertFrom-Json
$OutputJSON

#$OutputVirusTotal = Invoke-WebRequest -Method "POST" -Uri "https://www.virustotal.com/vtapi/v2/file/scan" -Body @{ apikey = $api; file = $Item }



function Set-VirusScan {
    param(
        [string]$file,
        [string]$api
    )
    If (!$api) {
        Write-Host "Please provide your api key Ex. Set-VirusScan -api {string} -file 'C:\temp\test.txt'"
    } Else {
        If (!$File) {
            Write-Host "Please provide a file. Ex: Set-VirusScan -api {string} -file 'C:\temp\test.txt'"
        } Else {
            #Upload the file to VirusTotal and grab the Resource ID
            Write-Host "Please wait wile the file is uploaded"
            ((Invoke-WebRequest -Method "POST" -Uri "https://www.virustotal.com/vtapi/v2/file/scan" -Form @{apikey = $api; file = (Get-Item $File) }).content | ConvertFrom-Json).resource | Set-Variable -Name VSResource
            Write-Host "File Uploaded"
            Write-Host "Retrieving VirusTotal Report"
            #Use Resource ID to pull the scan results
            Write-Host "Please wait wile VirusTotal scans your file"
            $randomsleeptime = Get-Random -Minimum 5 -Maximum 30
            Start-Sleep -Seconds $randomsleeptime
            $scanoutputuri = "https://www.virustotal.com/vtapi/v2/file/report?apikey=$api&resource=$VSResource&allinfo=true"
            $scanoutput = ((Invoke-WebRequest -Uri $scanoutputuri).content | ConvertFrom-Json)
            $permalink = ($scanoutput).permalink
            $resourceID = ($scanoutput).scan_id
            $scandate = ($scanoutput).scan_date
            $totalscans = ($scanoutput).total
            $positivedetects = ($scanoutput).positives
            Write-Host "Permalink: $permalink"
            Write-Host "Resource ID: $resourceID"
            Write-Host "Scan Date: $scandate"
            Write-Host "Total Scans: $totalscans"
            Write-Host "Positive Detects: $positivedetects"
        }
    }
}