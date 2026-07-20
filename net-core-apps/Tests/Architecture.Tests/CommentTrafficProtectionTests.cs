using System.Text.RegularExpressions;
using Xunit;

namespace Architecture.Tests;

public sealed class CommentTrafficProtectionTests
{
    [Fact]
    public void Current_comment_routes_apply_named_bounded_policies_and_advertise_rfc7807_429()
    {
        var routes = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "Services",
            "Comment",
            "Comment.Api",
            "AppInterfaces",
            "Routes",
            "Comments.route.cs"));
        var controller = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "Services",
            "Comment",
            "Comment.Api",
            "AppInterfaces",
            "Routes",
            "CommentsController.cs"));

        AssertRoute(
            routes,
            "cursorGroup.MapGet(\"/list\"",
            "TrafficProtectionPolicies.AnonymousView");
        AssertRoute(
            routes,
            "cursorGroup.MapGet(\"/owner/list\"",
            "TrafficProtectionPolicies.AnonymousView");
        AssertRoute(
            routes,
            "cursorGroup.MapGet(\"/{id:int}\"",
            "TrafficProtectionPolicies.AnonymousView");
        AssertRoute(
            routes,
            "cursorGroup.MapDelete(\"/{id:int}\"",
            "TrafficProtectionPolicies.AuthenticatedWrite");

        Assert.Equal(
            2,
            Regex.Matches(
                controller,
                @"EnableRateLimiting\(TrafficProtectionPolicies\.AuthenticatedWrite\)",
                RegexOptions.CultureInvariant).Count);
        Assert.Equal(
            2,
            Regex.Matches(
                controller,
                @"StatusCodes\.Status429TooManyRequests,\s*\""application/problem\+json\""",
                RegexOptions.CultureInvariant).Count);
    }

    private static void AssertRoute(string source, string marker, string policy)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing current Comment route '{marker}'.");
        var end = source.IndexOf(';', start);
        Assert.True(end > start, $"Unterminated current Comment route '{marker}'.");
        var registration = source[start..end];

        Assert.Contains($".RequireRateLimiting({policy})", registration, StringComparison.Ordinal);
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status429TooManyRequests)",
            registration,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the net-core-apps repository root.");
    }
}
