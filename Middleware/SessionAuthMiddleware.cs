using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OneClick_WebApp.Helpers;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OneClick_WebApp.Middleware
{
    public class SessionAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SessionAuthMiddleware> _logger;

        public SessionAuthMiddleware(RequestDelegate next, ILogger<SessionAuthMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, SessionAuthService sessionAuthService)
        {
            // Check if user has active session
            var userProfile = context.Session.GetObject<UserAccount>("UserProfile");

            if (userProfile != null)
            {
                _logger.LogDebug("Session middleware: Found active session for user {Email}", userProfile.Email);

                // Validate session data and create claims
                var claims = CreateUserClaims(userProfile);
                var identity = new ClaimsIdentity(claims, "Session");
                var principal = new ClaimsPrincipal(identity);
                context.User = principal;

                _logger.LogDebug("Session middleware: Set {ClaimCount} claims for user {Email}",
                    claims.Count, userProfile.Email);
            }
            else
            {
                _logger.LogDebug("Session middleware: No active session found");
            }

            await _next(context);
        }

        private List<Claim> CreateUserClaims(UserAccount userProfile)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userProfile.FirebaseUid ?? userProfile.Id),
                new Claim(ClaimTypes.Email, userProfile.Email),
                new Claim(ClaimTypes.Name, userProfile.Name ?? userProfile.Email),
                new Claim("FirebaseUid", userProfile.FirebaseUid ?? ""),
                new Claim("UserId", userProfile.Id ?? ""),
                new Claim("UserRole", userProfile.UserRole.ToString()),
                new Claim("IsErpUser", userProfile.IsErpUser.ToString())
            };

            // Add admin access claims
            bool hasAdminAccess = userProfile.UserRole == Role.Owner ||
                                userProfile.UserRole == Role.Admin ||
                                userProfile.UserRole == Role.Manager;

            if (hasAdminAccess)
            {
                claims.Add(new Claim("role", Role.Admin.ToString()));
                claims.Add(new Claim("isAdmin", "true"));
                claims.Add(new Claim("canAccessAdminPanel", "true"));
            }

            // Add role-specific claims
            switch (userProfile.UserRole)
            {
                case Role.Owner:
                    claims.Add(new Claim("isOwner", "true"));
                    claims.Add(new Claim("canManageSettings", "true"));
                    claims.Add(new Claim("canManageUsers", "true"));
                    claims.Add(new Claim("canViewReports", "true"));
                    break;
                case Role.Manager:
                    claims.Add(new Claim("isManager", "true"));
                    claims.Add(new Claim("canManageOrders", "true"));
                    claims.Add(new Claim("canViewReports", "true"));
                    break;
                case Role.Staff:
                    claims.Add(new Claim("isStaff", "true"));
                    claims.Add(new Claim("canViewOrders", "true"));
                    break;
                case Role.Customer:
                    claims.Add(new Claim("isCustomer", "true"));
                    break;
            }

            // Add branch access claims if ERP user
            if (userProfile.IsErpUser && userProfile.BranchAccess != null)
            {
                foreach (var branch in userProfile.BranchAccess.Where(b => b.Value))
                {
                    claims.Add(new Claim("BranchAccess", branch.Key));
                }
            }

            return claims;
        }
    }
}