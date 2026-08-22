using System.ComponentModel.DataAnnotations;

namespace MedHistory.Models;

public class LoginViewModel
{
    // ErrorMessage is a resource key, not finished copy: Program.cs points DataAnnotations
    // localization at SharedResource, so this English sentence is looked up there and falls back
    // to itself when there is no translation — the same contract every other key in the app has.
    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
