using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Infrastructure;

/// <summary>Runs test collections in ordinal order of their display name.</summary>
public sealed class ByNameCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        return testCollections.OrderBy(static c => c.DisplayName, StringComparer.Ordinal);
    }
}

/// <summary>The collection that runs before any other (see <see cref="ByNameCollectionOrderer"/>).</summary>
[CollectionDefinition(Name)]
public sealed class RunsFirstCollection
{
    public const string Name = "00 Runs first: before any window handle exists";
}
