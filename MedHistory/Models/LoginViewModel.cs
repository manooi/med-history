using System.ComponentModel.DataAnnotations;

namespace MedHistory.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
