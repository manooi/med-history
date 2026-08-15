namespace MedHistory.Models;

/// <summary>
/// One login POST outcome — logged so <see cref="Services.LoginThrottleRules"/> can throttle and
/// lock out repeated failures. A success clears the whole table rather than adding a row, so in
/// practice every stored row has <see cref="Succeeded"/> false; this table only ever holds a live
/// failure streak — see <c>AccountController.Login</c>.
/// </summary>
public class LoginAttempt
{
    public int Id { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    public bool Succeeded { get; set; }
}
