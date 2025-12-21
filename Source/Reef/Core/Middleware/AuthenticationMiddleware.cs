using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Reef.Core.Security;
using Serilog;

namespace Reef.Core.Middleware
{
    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly JwtTokenService _jwtTokenService;

        public AuthenticationMiddleware(RequestDelegate next, JwtTokenService jwtTokenService)
        {
            _next = next;
            _jwtTokenService = jwtTokenService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path;

            // These are the pages that require authentication
            var protectedPaths = new[]
            {
                "/admin",
                "/connections",
                "/dashboard",
                "/destinations",
                "/email-approvals",
                "/executions",
                "/groups",
                "/jobs",
                "/profiles",
                "/templates",
                "/documentation"
            };

            var isProtected = false;
            foreach (var protectedPath in protectedPaths)
            {
                if (path.Equals(protectedPath, StringComparison.OrdinalIgnoreCase) || path.Equals(protectedPath + ".html", StringComparison.OrdinalIgnoreCase))
                {
                    isProtected = true;
                    break;
                }
            }

            if (isProtected)
            {
                // Check if the cookie exists
                if (!context.Request.Cookies.TryGetValue("reef_token", out var token) || string.IsNullOrEmpty(token))
                {
                    Log.Debug("No authentication cookie found. Redirecting to /logoff.");
                    context.Response.Redirect("/logoff");
                    return;
                }

                // Validate the JWT token from the cookie
                var principal = _jwtTokenService.ValidateToken(token);
                if (principal == null)
                {
                    Log.Debug("Invalid or expired authentication token. Redirecting to /logoff.");

                    // Clear the invalid cookie
                    context.Response.Cookies.Delete("reef_token", new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = false,
                        SameSite = SameSiteMode.Lax,
                        Path = "/"
                    });

                    context.Response.Redirect("/logoff");
                    return;
                }

                // Set the user principal for downstream middleware/handlers
                context.User = principal;
            }

            await _next(context);
        }
    }
}
