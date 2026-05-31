using Filer.Modules.Auth.Domain;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace Filer.Modules.Auth.Tests.TestSupport;

/// <summary>
/// Builds a <see cref="UserManager{TUser}"/> mock. UserManager has no interface to
/// mock and a nine-argument constructor; only its virtual methods
/// (<c>FindByEmailAsync</c>, <c>CheckPasswordAsync</c>, <c>CreateAsync</c>) are set
/// up per test. Everything else is supplied as the minimal store plus nulls the
/// constructor tolerates.
/// </summary>
internal static class MockUserManager
{
    public static Mock<UserManager<ApplicationUser>> Create()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
