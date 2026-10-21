

using Ghanapoliticaldirectory.Domain;
using Microsoft.AspNetCore.Identity;

namespace Ghanapoliticaldirectory.Data.Seeding;

/// <summary>
/// Creates the roles, and (only if you configure one) a first administrator. There is no built-in password:
/// set Seed:AdminEmail and Seed:AdminPassword in user-secrets or environment variables for the first run,
/// then remove them.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(
        RoleManager<IdentityRole> roles, UserManager<ApplicationUser> users, string? adminEmail, string? adminPassword)
    {
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword)) return;

        var admin = await users.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true, DisplayName = "Administrator" };
            var created = await users.CreateAsync(admin, adminPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException("Could not create the first administrator: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        if (!await users.IsInRoleAsync(admin, Roles.Administrator))
            await users.AddToRoleAsync(admin, Roles.Administrator);
    }
}
