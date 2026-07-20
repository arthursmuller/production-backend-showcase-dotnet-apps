using System.Text.RegularExpressions;
using Xunit;

namespace ApiContract.Tests;

public sealed class RouteSourceContractTests
{
    [Fact]
    public void Comment_list_routes_have_one_canonical_minimal_route_owner()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiRoot = Path.Combine(repositoryRoot, "Services", "Comment", "Comment.Api");
        var routeSource = File.ReadAllText(Path.Combine(
            apiRoot, "AppInterfaces", "Routes", "Comments.route.cs"));
        var controllerSource = File.ReadAllText(Path.Combine(
            apiRoot, "AppInterfaces", "Routes", "CommentsController.cs"));

        Assert.Single(
            Regex.Matches(routeSource, "MapGet\\s*\\(\\s*\"/list\"")
                .Cast<Match>());
        Assert.Single(
            Regex.Matches(routeSource, "MapGet\\s*\\(\\s*\"/owner/list\"")
                .Cast<Match>());
        Assert.Empty(Regex.Matches(routeSource, "MapPost\\s*\\(\\s*\"/(owner/)?list\"")
            .Cast<Match>());
        Assert.Single(
            Regex.Matches(routeSource, "MapGroup\\s*\\(\\s*\"/api/comments\"")
                .Cast<Match>());
        Assert.Contains("WithName(\"ListCommentsCursor\")", routeSource, StringComparison.Ordinal);
        Assert.Contains("WithName(\"ListOwnerCommentsCursor\")", routeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy", routeSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deprecated", routeSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("obsolete", routeSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpGet(\"list\")]", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpGet(\"owner/list\")]", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost(\"list\")]", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost(\"owner/list\")]", controllerSource, StringComparison.Ordinal);
    }

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
