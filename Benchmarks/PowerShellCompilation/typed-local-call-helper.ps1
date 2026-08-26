function Add-One {
    param([long] $Value)

    [long] $result = $Value
    $result += 1
    return $result
}
