using Bunit;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Auth;
using Filer.Ui.Models;
using Filer.Ui.Pages;
using Filer.Ui.Tests.Auth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Pages;

public sealed class ProfilePageTests : BunitContext
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeAuthSession _session = new();

    public ProfilePageTests()
    {
        Services.AddSingleton<IAuthSession>(_session);
    }

    [Fact]
    public void Shows_the_current_user_after_loading()
    {
        _session.ProfileResults.Enqueue(new ProfileResult(
            new MeResponse { Email = "user@example.com", Id = UserId }, null));

        var cut = Render<Profile>();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".profile").TextContent.Should().Contain("user@example.com");
            cut.Find(".profile").TextContent.Should().Contain(UserId.ToString());
        });
    }

    [Fact]
    public void A_failed_load_shows_the_problem_and_retry_reloads()
    {
        _session.ProfileResults.Enqueue(new ProfileResult(null, new ProblemDetailsView
        {
            Title = "An unexpected error occurred",
            Status = 500,
        }));
        _session.ProfileResults.Enqueue(new ProfileResult(
            new MeResponse { Email = "user@example.com", Id = UserId }, null));

        var cut = Render<Profile>();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("An unexpected error occurred"));

        cut.Find(".error-retry").Click();

        cut.WaitForAssertion(() =>
            cut.Find(".profile").TextContent.Should().Contain("user@example.com"));
    }
}
