namespace SydneyBuddy.Api;

/// <summary>Server-side opt-in: a map or client setting cannot enable paid provider calls.</summary>
public sealed class PaidServicePolicy(IConfiguration configuration)
{
    public bool Allowed => configuration.GetValue<bool>("AllowPaidServices");
    public void RequireAllowed()
    {
        if (!Allowed)
            throw new ServiceException("Paid services are disabled. Use demo mode. For online testing, a developer must set AllowPaidServices=true and restart the backend.");
    }
}
