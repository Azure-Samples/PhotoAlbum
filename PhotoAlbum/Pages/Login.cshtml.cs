using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PhotoAlbum.Pages;

/// <summary>
/// Minimal admin login used to authenticate state-changing operations (CWE-306).
/// Credentials are read from configuration/environment (Admin:Username /
/// Admin:Password) and are never hard-coded.
/// </summary>
public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty]
    public string? Username { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var adminUser = _configuration["Admin:Username"] ?? "admin";
        var adminPassword = _configuration["Admin:Password"];

        // Fail closed if no admin password has been configured.
        if (string.IsNullOrEmpty(adminPassword))
        {
            ErrorMessage = "Admin account is not configured.";
            return Page();
        }

        var userMatches = string.Equals(Username, adminUser, StringComparison.Ordinal);
        var passwordMatches = FixedTimeEquals(Password, adminPassword);
        if (!userMatches || !passwordMatches)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, adminUser),
            new(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("/Index");
    }

    private static bool FixedTimeEquals(string? a, string? b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a ?? string.Empty);
        var bytesB = Encoding.UTF8.GetBytes(b ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
