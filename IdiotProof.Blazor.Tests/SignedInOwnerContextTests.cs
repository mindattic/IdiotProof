using System.Security.Claims;
using IdiotProof.Blazor.Auth;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The web front door's owner comes from the signed-in user's NameIdentifier claim, read on every
/// call; anonymous or a malformed claim is no owner, so callers fail closed.
/// </summary>
[TestFixture]
public sealed class SignedInOwnerContextTests
{
    private static ClaimsPrincipal SignedIn(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    [Test]
    public async Task Owner_IsTheSignedInUser_ReadPerCall()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var current = SignedIn(alice.ToString());
        var owner = new SignedInOwnerContext(() => Task.FromResult(current));

        var first = await owner.GetOwnerIdAsync();
        current = SignedIn(bob.ToString());
        var second = await owner.GetOwnerIdAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(alice));
            Assert.That(second, Is.EqualTo(bob), "never cached across a change of principal");
        });
    }

    [Test]
    public async Task Anonymous_OrMalformedClaim_IsNoOwner()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())]));

        var unauthenticated = await new SignedInOwnerContext(() => Task.FromResult(anonymous)).GetOwnerIdAsync();
        var malformed = await new SignedInOwnerContext(() => Task.FromResult(SignedIn("not-a-guid"))).GetOwnerIdAsync();
        var empty = await new SignedInOwnerContext(() => Task.FromResult(SignedIn(Guid.Empty.ToString()))).GetOwnerIdAsync();
        var nobody = await new SignedInOwnerContext(() => Task.FromResult(new ClaimsPrincipal())).GetOwnerIdAsync();

        Assert.Multiple(() =>
        {
            Assert.That(unauthenticated, Is.Null, "a claim on an unauthenticated identity is not an owner");
            Assert.That(malformed, Is.Null);
            Assert.That(empty, Is.Null);
            Assert.That(nobody, Is.Null);
        });
    }
}
