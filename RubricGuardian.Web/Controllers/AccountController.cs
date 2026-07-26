using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RubricGuardian.Web.Data;
using RubricGuardian.Web.Models;
using RubricGuardian.Web.Models.ViewModels;
using RubricGuardian.Web.Services;

namespace RubricGuardian.Web.Controllers;

public class AccountController : Controller
{
    private const int MaxLoginAttempts = 5;
    private static readonly TimeSpan LoginLockoutWindow = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IMemoryCache _cache;

    public AccountController(AppDbContext db, IPasswordHasher hasher, IMemoryCache cache)
    {
        _db = db; _hasher = hasher; _cache = cache;
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

        var email = vm.Email.Trim().ToLower();
        var attemptsKey = $"login-fail:{email}";
        // In-memory only: resets on restart and doesn't share state across multiple
        // instances - acceptable for this MVP's single-process deployment.
        if (_cache.TryGetValue<int>(attemptsKey, out var attempts) && attempts >= MaxLoginAttempts)
        {
            ModelState.AddModelError("", "Too many failed attempts. Try again in a few minutes.");
            return View(vm);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !_hasher.Verify(vm.Password, user.PasswordHash))
        {
            _cache.Set(attemptsKey, attempts + 1, LoginLockoutWindow);
            ModelState.AddModelError("", "Email or password is incorrect.");
            return View(vm);
        }

        _cache.Remove(attemptsKey);
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


    // ─── account management: the user's personal space ───────────────

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet("/account")]
    public async Task<IActionResult> Manage()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return Redirect("/login");

        var vm = new AccountViewModel
        {
            FullName = user.FullName,
            Email = user.Email,
            CreatedAt = user.CreatedAt,
            CourseCount = await _db.Courses.CountAsync(c => c.UserId == userId),
            AssignmentCount = await _db.Assignments.CountAsync(a => a.Course!.UserId == userId),
            SubmissionCount = await _db.Submissions.CountAsync(s => s.Assignment!.Course!.UserId == userId)
        };
        return View(vm);
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("/account/profile"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(UpdateProfileViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter your name.";
            return Redirect("/account");
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return Redirect("/login");

        user.FullName = vm.FullName.Trim();
        await _db.SaveChangesAsync();

        // refresh the auth cookie so the navbar shows the new name
        await SignInAsync(user);

        TempData["Success"] = "Name updated.";
        return Redirect("/account");
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("/account/password"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return Redirect("/login");

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Check the password fields: new password needs at least 8 characters and both entries must match.";
            return Redirect("/account");
        }
        if (!_hasher.Verify(vm.CurrentPassword, user.PasswordHash))
        {
            TempData["Error"] = "Current password is incorrect.";
            return Redirect("/account");
        }

        user.PasswordHash = _hasher.Hash(vm.NewPassword);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Password changed.";
        return Redirect("/account");
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
