using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RubricGuardian.Web.Data;
using RubricGuardian.Web.Models;
using RubricGuardian.Web.Models.ViewModels;
using RubricGuardian.Web.Services;

namespace RubricGuardian.Web.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;

    public AccountController(AppDbContext db, IPasswordHasher hasher)
    {
        _db = db; _hasher = hasher;
    }

    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null)
        => User.Identity?.IsAuthenticated == true
            ? RedirectToAction("Index", "Dashboard")
            : View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost("/login"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == vm.Email.Trim().ToLower());
        if (user is null || !_hasher.Verify(vm.Password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Email or password is incorrect.");
            return View(vm);
        }

        await SignInAsync(user);
        return LocalRedirect(vm.ReturnUrl ?? "/dashboard");
    }

    [HttpGet("/register")]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost("/register"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var email = vm.Email.Trim().ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError(nameof(vm.Email), "An account with this email already exists.");
            return View(vm);
        }

        var user = new User { FullName = vm.FullName.Trim(), Email = email, PasswordHash = _hasher.Hash(vm.Password) };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await SignInAsync(user);
        return Redirect("/dashboard");
    }

    [HttpPost("/logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }

    private async Task SignInAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
    }
}
