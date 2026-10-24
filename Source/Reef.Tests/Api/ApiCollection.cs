using Xunit;

namespace Reef.Tests.Api;

// Reef boots from process-wide environment variables, so API tests share one host.
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ReefApiFactory>
{
    public const string Name = "Api";
}
