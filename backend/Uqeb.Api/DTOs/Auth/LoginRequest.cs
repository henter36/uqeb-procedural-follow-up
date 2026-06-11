using System.ComponentModel.DataAnnotations;

namespace Uqeb.Api.DTOs.Auth;

public class LoginRequest
{
    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    public string Password { get; set; } = string.Empty;
}
