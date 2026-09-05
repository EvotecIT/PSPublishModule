function Resolve-RuleValue {
    [OutputType([int])]
    param(
        [int] $Code
    )

    switch ($Code) {
        1 { return 10 }
        2 { return 20 }
        3 { return 30 }
        default { return -1 }
    }
}
