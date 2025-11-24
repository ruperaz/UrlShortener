namespace IdentityService.DTOs;

public record RegisterRequest(string UserName, string Email, string Password);
public record LoginRequest(string UserNameOrEmail, string Password);
public record AuthResponse(string AccessToken, string UserId, string UserName, string Email);