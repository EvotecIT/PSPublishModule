Console.WriteLine($"Native smoke: {Environment.GetEnvironmentVariable("LANG") ?? "default locale"}");
return args.Length > 0 && args[0] == "fail" ? 1 : 0;
