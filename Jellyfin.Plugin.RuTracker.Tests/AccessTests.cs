using System;
using System.Globalization;
using System.Security.Claims;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Configuration;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class AccessTests
{
    private static readonly Guid Searcher = Guid.NewGuid();
    private static readonly Guid Downloader = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static PluginConfiguration Config() => new()
    {
        SearchUserIds = [Searcher],
        DownloadUserIds = [Downloader]
    };

    [Fact]
    public void Policy_SearchOnlyUser()
    {
        var access = AccessPolicy.Evaluate(Searcher, false, false, Config());

        Assert.True(access.Has(AccessRole.Search));
        Assert.False(access.Has(AccessRole.Download));
    }

    [Fact]
    public void Policy_DownloadImpliesSearch()
    {
        var access = AccessPolicy.Evaluate(Downloader, false, false, Config());

        Assert.True(access.Has(AccessRole.Search));
        Assert.True(access.Has(AccessRole.Download));
    }

    [Fact]
    public void Policy_UnlistedUser_HasNothing()
        => Assert.Equal(AccessInfo.None, AccessPolicy.Evaluate(Stranger, false, false, Config()));

    [Fact]
    public void Policy_AdministratorHasEverything()
        => Assert.Equal(new AccessInfo(true, true, true), AccessPolicy.Evaluate(Stranger, true, false, Config()));

    [Fact]
    public void Policy_DisabledUser_HasNothingEvenIfAdmin()
        => Assert.Equal(AccessInfo.None, AccessPolicy.Evaluate(Downloader, true, true, Config()));

    [Fact]
    public void Service_UsesUserManagerPermissions()
    {
        var admin = new User("admin", "auth", "reset");
        admin.AddDefaultPermissions();
        admin.SetPermission(PermissionKind.IsAdministrator, true);

        var plain = new User("plain", "auth", "reset");
        plain.AddDefaultPermissions();

        var adminId = Guid.NewGuid();
        var userManager = new Mock<IUserManager>();
        userManager.Setup(m => m.GetUserById(adminId)).Returns(admin);
        userManager.Setup(m => m.GetUserById(Searcher)).Returns(plain);

        var accessor = new Mock<IPluginConfigurationAccessor>();
        accessor.Setup(a => a.Current).Returns(Config());

        var service = new AccessService(userManager.Object, accessor.Object);

        Assert.True(service.GetAccess(adminId).CanDownload);
        Assert.True(service.GetAccess(Searcher).CanSearch);
        Assert.False(service.GetAccess(Searcher).CanDownload);
        Assert.Equal(AccessInfo.None, service.GetAccess(Stranger)); // unknown to user manager
    }

    [Fact]
    public void CallerIdentity_ParsesJellyfinClaims()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("Jellyfin-UserId", id.ToString("N", CultureInfo.InvariantCulture)),
            new Claim("Jellyfin-IsApiKey", "False")
        ]));

        Assert.Equal(id, CallerIdentity.GetUserId(principal));
        Assert.False(CallerIdentity.IsApiKey(principal));
        Assert.Equal(Guid.Empty, CallerIdentity.GetUserId(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
