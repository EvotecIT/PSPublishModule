# PowerForge.Versioning

Read or update version declarations in project files, PowerShell module
manifests, and supported build scripts. The library works directly with
the source files, without starting PowerShell or evaluating an MSBuild project.

```csharp
using PowerForge;

var versions = new ProjectVersionService().Discover(new ProjectVersionQueryRequest
{
    RootPath = "/path/to/project"
});

foreach (var item in versions)
    Console.WriteLine($"{item.Source}: {item.Version}");
```

PowerForge already references this library. A project that only needs the
versioning API can reference PowerForge.Versioning directly.
