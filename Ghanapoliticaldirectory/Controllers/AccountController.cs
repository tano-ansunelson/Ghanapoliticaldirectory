
using Ghanapoliticaldirectory.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Ghanapoliticaldirectory.Controllers;

public class LoginForm
{
    [Required(ErrorMessage = "Enter your email address."), EmailAddress]
    public string? Email { get; set; }
    [Required(ErrorMessage = "Enter your password."), DataType(DataType.Password)]
    public string? Password { get; set; }
    public string? ReturnUrl { get; set; }
}

[Route("account")]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;

    public AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users)
    { _signIn = signIn; _users = users; }

    [HttpGet("login"), AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginForm { ReturnUrl = returnUrl });

    [HttpPost("login"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var user = await _users.FindByEmailAsync(form.Email!);
        // One generic message for every failure, so the form never reveals which emails exist.
        const string generic = "The email or password is not correct, or the account is locked.";
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError("", generic);
            return View(form);
        }

        var result = await _signIn.PasswordSignInAsync(user, form.Password!, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", generic);
            return View(form);
        }

        return Url.IsLocalUrl(form.ReturnUrl) ? LocalRedirect(form.ReturnUrl!) : RedirectToAction("Dashboard", "Admin");
    }

    [HttpPost("logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("denied"), AllowAnonymous]
    public IActionResult Denied() => View();
}
