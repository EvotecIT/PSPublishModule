param(
    [int] $Value = 42,
    [EnvironmentVariableTarget] $Target = ([EnvironmentVariableTarget]::User)
)
"$Value|$Target"
