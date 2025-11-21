using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Http;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Services;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OneClick_WebApp.Middleware
{
    public class JwtCookieMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<JwtCookieMiddleware> _logger;

        public JwtCookieMiddleware(RequestDelegate next, ILogger<JwtCookieMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, FirebaseDbService dbService, FirebaseAuthService authService)
        {
            // Check if there's a Firebase token in the cookies
            if (context.Request.Cookies.TryGetValue("firebaseToken", out var token))
            {
                try
                {
                    // Verify the token with Firebase
                    var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);
                    var uid = decodedToken.Uid;

                    _logger.LogInformation("JWT Middleware: Processing token for UID: {Uid}", uid);

                    // Get the authenticated user data (checks both ERP and online users)
                    var authenticatedUser = await authService.GetAuthenticatedUserAsync(uid);

                    if (authenticatedUser != null)
                    {
                        _logger.LogInformation("JWT Middleware: Found user {Email} with role {Role}, IsErpUser: {IsErpUser}",
                            authenticatedUser.Email, authenticatedUser.UserRole, authenticatedUser.IsErpUser);

                        // Create claims list
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.NameIdentifier, uid),
                            new Claim(ClaimTypes.Email, authenticatedUser.Email),
                            new Claim(ClaimTypes.Name, authenticatedUser.Name ?? authenticatedUser.Email),
                            new Claim("FirebaseUid", uid),
                            new Claim("UserRole", authenticatedUser.UserRole.ToString()),
                            new Claim("IsErpUser", authenticatedUser.IsErpUser.ToString())
                        };

                        
                        // Owners (7) and Admins (9) both get admin access
                        bool hasAdminAccess = authenticatedUser.UserRole == Role.Owner ||
                                            authenticatedUser.UserRole == Role.Admin ||
                                            authenticatedUser.UserRole == Role.Manager; // Optionally include Manager

                        if (hasAdminAccess)
                        {
                            claims.Add(new Claim("role", Role.Admin.ToString())); // This satisfies the "AdminOnly" policy
                            claims.Add(new Claim("isAdmin", "true"));
                            claims.Add(new Claim("canAccessAdminPanel", "true"));

                            _logger.LogInformation("JWT Middleware: User {Email} granted admin access (Role: {Role})",
                                authenticatedUser.Email, authenticatedUser.UserRole);
                        }

                        // Add specific role-based claims for granular control
                        switch (authenticatedUser.UserRole)
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
                        if (authenticatedUser.IsErpUser && authenticatedUser.BranchAccess != null)
                        {
                            foreach (var branch in authenticatedUser.BranchAccess.Where(b => b.Value))
                            {
                                claims.Add(new Claim("BranchAccess", branch.Key));
                            }
                        }

                        // Set the user identity
                        var identity = new ClaimsIdentity(claims, "Firebase");
                        var principal = new ClaimsPrincipal(identity);
                        context.User = principal;

                        _logger.LogInformation("JWT Middleware: Successfully authenticated user {Email} with {ClaimCount} claims",
                            authenticatedUser.Email, claims.Count);
                    }
                    else
                    {
                        _logger.LogWarning("JWT Middleware: No user data found for UID: {Uid}", uid);
                    }
                }
                catch (FirebaseAuthException ex)
                {
                    _logger.LogError(ex, "JWT Middleware: Token validation failed");
                    // Token is invalid or expired - remove it
                    context.Response.Cookies.Delete("firebaseToken");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "JWT Middleware: Unexpected error processing token");
                }
            }

            await _next(context);
        }
    }
}