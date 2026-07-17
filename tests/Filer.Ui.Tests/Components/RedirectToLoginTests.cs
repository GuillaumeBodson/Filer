using Bunit;
using Bunit.TestDoubles;
using Filer.Ui.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Components;

/// <summary>
/// The route guard (#133): anonymous users reaching a protected page are sent to
/// /login carrying the intended target, so sign-in can return them there.
/// </summary>
public sealed class RedirectToLoginTests : BunitContext
{
    [Fact]
    public void Redirects_to_login_carrying_the_intended_target()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("profile");

        Render<RedirectToLogin>();

        nav.Uri.Should().Be($"{nav.BaseUri}login?returnUrl=profile");
    }

    [Fact]
    public void Redirects_to_plain_login_from_the_root()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<RedirectToLogin>();

        nav.Uri.Should().Be($"{nav.BaseUri}login");
    }

    [Fact]
    public void Escapes_query_strings_in_the_target()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("documents?folderId=42");

        Render<RedirectToLogin>();

        nav.Uri.Should().Be($"{nav.BaseUri}login?returnUrl=documents%3FfolderId%3D42");
    }
}
