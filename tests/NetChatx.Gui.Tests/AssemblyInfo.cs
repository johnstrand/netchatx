using Xunit;

// Disable parallel test execution within GUI tests to prevent Avalonia property registry race conditions
[assembly: CollectionBehavior(DisableTestParallelization = true)]
