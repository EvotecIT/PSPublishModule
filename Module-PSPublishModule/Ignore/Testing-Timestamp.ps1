# read in the certificate from a pre-existing PFX file


# https://stackoverflow.csom/questions/912955/how-can-i-prevent-needing-to-re-sign-my-code-every-1-or-2-years



Register-Certificate -Path 'C:\Support\GitHub\PSPublishModule\Ignore\Old' -LocalStore CurrentUser #-CertificatePFX "C:\Support\Important\Certificates\Przemyslaw Klys EVOTEC.pfx"

