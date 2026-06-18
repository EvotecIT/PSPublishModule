---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetServiceHealthCheck
## SYNOPSIS
Creates an HTTP readiness check for DotNet publish service lifecycle verification.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetServiceHealthCheck -Uri <string> [-Id <string>] [-ExpectedStatusCode <int>] [-JsonPath <string>] [-ExpectedValue <string>] [-TimeoutSeconds <int>] [-PollIntervalMilliseconds <int>] [-OnFailure <DotNetPublishPolicyMode>] [<CommonParameters>]
```

## DESCRIPTION
Creates an HTTP readiness check for DotNet publish service lifecycle verification.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetServiceHealthCheck -Id Runtime -Uri 'http://127.0.0.1:58433/runtime' -JsonPath 'status' -ExpectedValue 'Ready'
```


## PARAMETERS

### -ExpectedStatusCode
Expected HTTP status code. Defaults to 200.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedValue
Optional expected JSON scalar value at JsonPath.
When omitted, the check only verifies that the path exists.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Id
Optional check identifier used in logs and failure messages.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JsonPath
Optional dot-separated JSON path to validate in the response body.
Array indexes can be expressed as property[0].

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OnFailure
Policy used when the health check does not pass before timeout.

```yaml
Type: DotNetPublishPolicyMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Fail, Skip

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PollIntervalMilliseconds
Delay in milliseconds between polling attempts.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TimeoutSeconds
Maximum time in seconds to wait for the endpoint to pass.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Uri
Absolute HTTP or HTTPS endpoint to poll.
Supports service lifecycle tokens such as {serviceName}, {outputDir}, and {executablePath}.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishServiceHealthCheck`

## RELATED LINKS

- None
