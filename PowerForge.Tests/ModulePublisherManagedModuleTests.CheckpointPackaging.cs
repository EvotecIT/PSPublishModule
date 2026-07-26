namespace PowerForge.Tests;

public sealed partial class ModulePublisherManagedModuleTests
{
    [Fact]
    public void CreateCheckpointedPublishPlan_PreservesApprovedPackagingConfiguration()
    {
        var information = new InformationConfiguration
        {
            IncludeRoot = ["README.md"],
            IncludePS1 = ["Commands"],
            IncludeAll = ["Assets"],
            ExcludeFromPackage = ["Private"]
        };
        var delivery = new DeliveryOptionsConfiguration
        {
            Enable = true,
            InternalsPath = "Payload"
        };

        var plan = ModulePublisher.CreateCheckpointedPublishPlan(new ModuleCheckpointPublishRequest
        {
            ProjectRoot = Path.GetTempPath(),
            ModuleName = "Sample",
            ModuleVersion = "1.2.3",
            ModulePath = Path.Combine(Path.GetTempPath(), "Sample"),
            Information = information,
            Delivery = delivery
        });

        Assert.Same(information, plan.Information);
        Assert.Same(delivery, plan.Delivery);
    }
}
