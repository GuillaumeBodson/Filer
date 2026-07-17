using Filer.Ui.Auth;
using Filer.Ui.Models;

namespace Filer.Ui.Tests.Auth;

/// <summary>Scriptable <see cref="IAuthSession"/> for page tests; records every call.</summary>
internal sealed class FakeAuthSession : IAuthSession
{
    public ProblemDetailsView? NextLoginResult { get; set; }
    public ProblemDetailsView? NextRegisterResult { get; set; }
    public Queue<ProfileResult> ProfileResults { get; } = new();

    public List<(string Email, string Password)> LoginCalls { get; } = [];
    public List<(string Email, string Password)> RegisterCalls { get; } = [];
    public int LogoutCalls { get; private set; }

    public Task<ProblemDetailsView?> LoginAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        LoginCalls.Add((email, password));
        return Task.FromResult(NextLoginResult);
    }

    public Task<ProblemDetailsView?> RegisterAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        RegisterCalls.Add((email, password));
        return Task.FromResult(NextRegisterResult);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        LogoutCalls++;
        return Task.CompletedTask;
    }

    public Task<ProfileResult> GetProfileAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ProfileResults.Dequeue());
}
