using Bunit;
using Bunit.TestDoubles;
using Filer.Ui.Auth;
using Filer.Ui.Models;
using Filer.Ui.Pages;
using Filer.Ui.Tests.Auth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Pages;

public sealed class LoginPageTests : BunitContext
{
    private readonly FakeAuthSession _session = new();

    public LoginPageTests()
    {
        Services.AddSingleton<IAuthSession>(_session);
    }

    // SupplyParameterFromQuery binds from the URI, so the page is rendered "at" /login
    // exactly like the router would.
    private IRenderedComponent<Login> RenderPage(string? returnUrl = null)
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo(returnUrl is null
            ? "login"
            : $"login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return Render<Login>();
    }

    [Fact]
    public void Fields_are_labelled_for_accessibility()
    {
        var cut = RenderPage();

        cut.Find("label[for=email]").TextContent.Should().Be("Email");
        cut.Find("label[for=password]").TextContent.Should().Be("Password");
        cut.Find("#password").GetAttribute("type").Should().Be("password");
    }

    [Fact]
    public void Empty_submit_shows_validation_and_calls_nothing()
    {
        var cut = RenderPage();

        cut.Find("form").Submit();

        cut.FindAll(".validation-message").Count.Should().Be(2);
        _session.LoginCalls.Should().BeEmpty();
    }

    [Fact]
    public void Successful_sign_in_navigates_home_by_default()
    {
        var cut = RenderPage();
        cut.Find("#email").Change("user@example.com");
        cut.Find("#password").Change("s3cure-pass");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            _session.LoginCalls.Should().ContainSingle().Which.Should().Be(("user@example.com", "s3cure-pass"));
            var nav = Services.GetRequiredService<BunitNavigationManager>();
            nav.Uri.Should().Be(nav.BaseUri);
        });
    }

    [Fact]
    public void Successful_sign_in_returns_to_the_guarded_target()
    {
        var cut = RenderPage(returnUrl: "profile");
        cut.Find("#email").Change("user@example.com");
        cut.Find("#password").Change("s3cure-pass");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Services.GetRequiredService<BunitNavigationManager>().Uri.Should().EndWith("/profile"));
    }

    [Fact]
    public void An_absolute_return_url_is_ignored_to_prevent_an_open_redirect()
    {
        var cut = RenderPage(returnUrl: "https://evil.test/phish");
        cut.Find("#email").Change("user@example.com");
        cut.Find("#password").Change("s3cure-pass");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var nav = Services.GetRequiredService<BunitNavigationManager>();
            nav.Uri.Should().Be(nav.BaseUri);
        });
    }

    [Fact]
    public void Rejected_credentials_render_the_problem_and_stay_on_the_page()
    {
        _session.NextLoginResult = new ProblemDetailsView
        {
            Title = "Authentication failed",
            Detail = "Invalid email or password.",
            Status = 401,
            Code = "invalid_credentials",
        };
        var cut = RenderPage();
        cut.Find("#email").Change("user@example.com");
        cut.Find("#password").Change("wrong-pass");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[role=alert]").TextContent.Should().Contain("Invalid email or password.");
            Services.GetRequiredService<BunitNavigationManager>().Uri.Should().EndWith("/login");
        });
    }
}
