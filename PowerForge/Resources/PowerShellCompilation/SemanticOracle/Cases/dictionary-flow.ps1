$Values = [ordered] @{ First = 42; Second = 'ready' }
if ($Values.First -is [int]) { return 42 } else { return 0 }
