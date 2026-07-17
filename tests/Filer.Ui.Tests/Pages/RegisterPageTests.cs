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

public sealed class RegisterPageTests : BunitContext
{
    private readonly FakeAuthSession _session = new();

    public RegisterPageTests()
    {
        Services.AddSingleton<IAuthSession>(_session);
    }

    private IRenderedComponent<Register> RenderPage() => Render<Register>();

    private static void Fill(IRenderedComponent<Register> cut,
        string email = "new@example.com", string password = "s3cure-pass", string? confirm = null)
    {
        cut.Find("#email").Change(email);
        cut.Find("#password").Change(password);
        cut.Find("#confirm-password").Change(confirm ?? password);
    }

    [Fact]
    public void Mismatched_passwords_fail_client_side_without_a_server_call()
    {
        var cut = RenderPage();
        Fill(cut, confirm: "different-pass");

        cut.Find("form").Submit();

        cut.FindAll(".validation-message").Should().ContainSingle()
            .Which.TextContent.Should().Be("Passwords do not match.");
        _session.RegisterCalls.Should().BeEmpty();
    }

    [Fact]
    public void A_short_password_fails_the_mirrored_server_rule()
    {
        var cut = RenderPage();
        Fill(cut, password: "short", confirm: "short");

        cut.Find("form").Submit();

        cut.FindAll(".validation-message").Should().Contain(
            m => m.TextContent.Contains("at least 8 characters"));
        _session.RegisterCalls.Should().BeEmpty();
    }

    [Fact]
    public void Successful_registration_signs_in_and_navigates_home()
    {
        var cut = RenderPage();
        Fill(cut);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            _session.RegisterCalls.Should().ContainSingle().Which.Should().Be(("new@example.com", "s3cure-pass"));
            var nav = Services.GetRequiredService<BunitNavigationManager>();
            nav.Uri.Should().Be(nav.BaseUri);
        });
    }

    [Fact]
    public void A_taken_email_renders_the_server_problem()
    {
        _session.NextRegisterResult = new ProblemDetailsView
        {
            Title = "Conflict",
            Detail = "An account with this email already exists.",
            Status = 409,
            Code = "email_taken",
        };
        var cut = RenderPage();
        Fill(cut);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("An account with this email already exists."));
    }
}
