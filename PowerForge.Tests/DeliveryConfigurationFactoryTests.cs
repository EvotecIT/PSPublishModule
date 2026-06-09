using PowerForge;

namespace PowerForge.Tests;

public sealed class DeliveryConfigurationFactoryTests
{
    [Fact]
    public void Create_returns_null_when_delivery_disabled()
    {
        var factory = new DeliveryConfigurationFactory();

        var segment = factory.Create(new DeliveryConfigurationRequest
        {
            Enable = false
        });

        Assert.Null(segment);
    }

    [Fact]
    public void Create_normalizes_paths_links_and_command_names()
    {
        var factory = new DeliveryConfigurationFactory();

        var segment = factory.Create(new DeliveryConfigurationRequest
        {
            Enable = true,
            Sign = true,
            InternalsPath = " Internals ",
            IncludeRootReadme = true,
            ImportantLinks =
            [
                new DeliveryImportantLink { Title = " Docs ", Url = " https://example.test/docs " },
                new DeliveryImportantLink { Title = " ", Url = "https://ignored.test" }
            ],
            PreservePaths = [" Config/** ", "config/**", "  "],
            OverwritePaths = [" Bin/** ", "Templates/**", "bin/**"],
            InstallCommandName = " Install-ContosoToolkit ",
            UpdateCommandName = " Update-ContosoToolkit "
        });

        var delivery = Assert.IsType<DeliveryOptionsConfiguration>(Assert.IsType<ConfigurationOptionsSegment>(segment).Options.Delivery);
        Assert.True(delivery.Enable);
        Assert.True(delivery.Sign);
        Assert.Equal(" Internals ", delivery.InternalsPath);
        Assert.True(delivery.IncludeRootReadme);
        Assert.True(delivery.GenerateInstallCommand);
        Assert.True(delivery.GenerateUpdateCommand);
        Assert.Equal("Install-ContosoToolkit", delivery.InstallCommandName);
        Assert.Equal("Update-ContosoToolkit", delivery.UpdateCommandName);
        var preservePaths = Assert.IsType<string[]>(delivery.PreservePaths);
        var overwritePaths = Assert.IsType<string[]>(delivery.OverwritePaths);
        Assert.Equal(["Config/**"], preservePaths);
        Assert.Equal(["Bin/**", "Templates/**"], overwritePaths);

        var links = Assert.IsType<DeliveryImportantLink[]>(delivery.ImportantLinks);
        var link = Assert.Single(links);
        Assert.Equal("Docs", link.Title);
        Assert.Equal("https://example.test/docs", link.Url);
    }
}
